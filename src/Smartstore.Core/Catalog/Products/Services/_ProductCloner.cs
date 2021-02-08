﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Smartstore.ComponentModel;
using Smartstore.Core.Catalog.Attributes;
using Smartstore.Core.Catalog.Brands;
using Smartstore.Core.Catalog.Categories;
using Smartstore.Core.Catalog.Pricing;
using Smartstore.Core.Content.Media;
using Smartstore.Core.Content.Seo;
using Smartstore.Core.Data;
using Smartstore.Core.Localization;
using Smartstore.Core.Stores;
using Smartstore.Data;
using Smartstore.Diagnostics;

namespace Smartstore.Core.Catalog.Products
{
    public partial class ProductCloner : IProductCloner
    {
        private readonly SmartDbContext _db;
        private readonly IChronometer _chronometer;
        private readonly ILanguageService _languageService;
        private readonly IStoreMappingService _storeMappingService;
        private readonly ILocalizedEntityService _localizedEntityService;
        private readonly IProductAttributeMaterializer _productAttributeMaterializer;
        private readonly IUrlService _urlService;

        public ProductCloner(
            SmartDbContext db,
            IChronometer chronometer,
            ILanguageService languageService,
            IStoreMappingService storeMappingService,
            ILocalizedEntityService localizedEntityService,
            IProductAttributeMaterializer productAttributeMaterializer,
            IUrlService urlService)
        {
            _db = db;
            _chronometer = chronometer;
            _languageService = languageService;
            _storeMappingService = storeMappingService;
            _localizedEntityService = localizedEntityService;
            _productAttributeMaterializer = productAttributeMaterializer;
            _urlService = urlService;
        }

        public Localizer T { get; set; } = NullLocalizer.Instance;

        public virtual async Task<Product> CloneProductAsync(
            Product product,
            string cloneName,
            bool isPublished,
            bool copyAssociatedProducts = true)
        {
            Guard.NotNull(product, nameof(product));
            Guard.NotEmpty(cloneName, nameof(cloneName));

            var clone = new Product();
            var utcNow = DateTime.UtcNow;
            var languages = await _languageService.GetAllLanguagesAsync(true);
            int? sampleDownloadId = null;

            using (_chronometer.Step("Copy product " + product.Id))
            using (var scope = new DbContextScope(_db, autoDetectChanges: false, hooksEnabled: false, deferCommit: true, forceNoTracking: true))
            {
                if (product.HasSampleDownload && product.SampleDownload != null)
                {
                    var sampleDownloadClone = product.SampleDownload.Clone();
                    _db.Downloads.Add(sampleDownloadClone);

                    await scope.CommitAsync();
                    sampleDownloadId = sampleDownloadClone.Id;
                }

                var props = FastProperty.GetProperties(typeof(Product), PropertyCachingStrategy.EagerCached);

                foreach (var prop in props.Values)
                {
                    if (prop.IsComplexType)
                        continue;

                    if (!prop.IsPublicSettable)
                        continue;

                    prop.SetValue(clone, prop.GetValue(product));
                }

                clone.Id = 0;
                clone.Name = cloneName;
                clone.SampleDownloadId = sampleDownloadId;
                clone.Published = isPublished;
                clone.CreatedOnUtc = utcNow;
                clone.UpdatedOnUtc = utcNow;

                // Category mappings.
                clone.ProductCategories.AddRange(product.ProductCategories.Select(x => new ProductCategory
                {
                    CategoryId = x.CategoryId,
                    IsFeaturedProduct = x.IsFeaturedProduct,
                    DisplayOrder = x.DisplayOrder
                }));

                // Manufacturer mappings.
                clone.ProductManufacturers.AddRange(product.ProductManufacturers.Select(x => new ProductManufacturer
                {
                    ManufacturerId = x.ManufacturerId,
                    IsFeaturedProduct = x.IsFeaturedProduct,
                    DisplayOrder = x.DisplayOrder
                }));

                // Media file mappings.
                clone.ProductPictures.AddRange(product.ProductPictures.Select(x => new ProductMediaFile
                {
                    MediaFileId = x.MediaFileId,
                    DisplayOrder = x.DisplayOrder
                }));
                if (clone.MainPictureId == null)
                {
                    clone.MainPictureId = product.ProductPictures.FirstOrDefault()?.MediaFileId;
                }

                // Product specification attributes.
                clone.ProductSpecificationAttributes.AddRange(product.ProductSpecificationAttributes.Select(x => new ProductSpecificationAttribute
                {
                    SpecificationAttributeOptionId = x.SpecificationAttributeOptionId,
                    AllowFiltering = x.AllowFiltering,
                    ShowOnProductPage = x.ShowOnProductPage,
                    DisplayOrder = x.DisplayOrder
                }));

                // Tier prices.
                clone.TierPrices.AddRange(product.TierPrices.Select(x => new TierPrice
                {
                    StoreId = x.StoreId,
                    CustomerRoleId = x.CustomerRoleId,
                    Quantity = x.Quantity,
                    Price = x.Price,
                    CalculationMethod = x.CalculationMethod
                }));
                clone.HasTierPrices = clone.TierPrices.Any();

                // Discount mappings.
                foreach (var discount in product.AppliedDiscounts)
                {
                    clone.AppliedDiscounts.Add(discount);
                    clone.HasDiscountsApplied = true;
                }

                // Tags.
                foreach (var tag in product.ProductTags)
                {
                    clone.ProductTags.Add(tag);
                }

                // >>>>>>> Put clone to db (from here on we need the product clone's ID).
                _db.Products.Add(clone);
                await scope.CommitAsync();

                // Store mappings.
                var selectedStoreIds = await _storeMappingService.GetAuthorizedStoreIdsAsync(product);
                selectedStoreIds.Each(id => _storeMappingService.AddStoreMapping(clone, id));

                await ProcessPromotions(product, clone);

                await ProcessSlugs(product, clone, languages);

                await ProcessLocalization(product, clone, languages);

                await ProcessBundleItems(product, clone);

                await ProcessDownloads(product, clone);

                // >>>>>>> Put to db.
                await scope.CommitAsync();                

                // Attributes and attribute combinations.
                await ProcessAttributes(scope, product, clone, languages);

                // Update computed properties.
                clone.LowestAttributeCombinationPrice = await _db.ProductVariantAttributeCombinations
                    .ApplyLowestPriceFilter(clone.Id)
                    .Select(x => x.Price)
                    .FirstOrDefaultAsync();

                // Associated products.
                if (copyAssociatedProducts && product.ProductType != ProductType.BundledProduct)
                {
                    await ProcessAssociatedProducts(product, clone, isPublished);
                }

                // >>>>>>> Our final commit.
                await scope.CommitAsync();
            }

            return clone;
        }

        private async Task ProcessSlugs(Product product, Product clone, IEnumerable<Language> languages)
        {
            var slugResult = await _urlService.ValidateSlugAsync(clone, clone.Name, true);
            await _urlService.ApplySlugAsync(slugResult, true);

            foreach (var lang in languages)
            {
                string name = product.GetLocalized(x => x.Name, lang, false, false);

                slugResult = await _urlService.ValidateSlugAsync(clone, name, false, lang.Id);
                await _urlService.ApplySlugAsync(slugResult, true);
            }
        }

        private async Task ProcessPromotions(Product product, Product clone)
        {
            // Related products mappings.
            // Join products to ignore deleted products.
            var relatedProductsQuery =
                from rp in _db.RelatedProducts.AsNoTracking()
                join p in _db.Products.AsNoTracking() on rp.ProductId2 equals p.Id
                where rp.ProductId1 == product.Id
                orderby rp.DisplayOrder
                select rp;

            var relatedProducts = await relatedProductsQuery.ToListAsync();

            _db.RelatedProducts.AddRange(relatedProducts.Select(x => new RelatedProduct
            {
                ProductId1 = clone.Id,
                ProductId2 = x.ProductId2,
                DisplayOrder = x.DisplayOrder
            }));

            // Cross-sell products mappings.
            // Join products to ignore deleted products.
            var crossSellProductsQuery =
                from csp in _db.CrossSellProducts.AsNoTracking()
                join p in _db.Products.AsNoTracking() on csp.ProductId2 equals p.Id
                where csp.ProductId1 == product.Id
                orderby csp.Id
                select csp;

            var crossSellProducts = await crossSellProductsQuery.ToListAsync();

            _db.CrossSellProducts.AddRange(crossSellProducts.Select(x => new CrossSellProduct
            {
                ProductId1 = clone.Id,
                ProductId2 = x.ProductId2
            }));
        }

        private async Task ProcessLocalization(Product product, Product clone, IEnumerable<Language> languages)
        {
            var keySelectors = new List<Expression<Func<Product, string>>>
            {
                x => x.Name,
                x => x.ShortDescription,
                x => x.FullDescription,
                x => x.MetaKeywords,
                x => x.MetaDescription,
                x => x.MetaTitle,
                x => x.BundleTitleText
            };

            foreach (var lang in languages)
            {
                foreach (var keySelector in keySelectors)
                {
                    string value = product.GetLocalized(keySelector, lang, false, false);
                    if (value.HasValue())
                    {
                        await _localizedEntityService.ApplyLocalizedValueAsync(clone, keySelector, value, lang.Id);
                    }                    
                }
            }
        }

        private async Task ProcessAttributes(DbContextScope scope, Product product, Product clone, IEnumerable<Language> languages)
        {
            await _db.LoadCollectionAsync(product, x => x.ProductVariantAttributes);
            await _db.LoadCollectionAsync(product, x => x.ProductVariantAttributeCombinations);

            // Former attribute id > clone.
            var attributeMap = new Dictionary<int, ProductVariantAttribute>();
            // Former attribute value id > clone.
            var valueMap = new Dictionary<int, ProductVariantAttributeValue>();

            // Product attributes.
            foreach (var pva in product.ProductVariantAttributes)
            {
                var attributeClone = new ProductVariantAttribute
                {
                    ProductId = clone.Id,
                    ProductAttributeId = pva.ProductAttributeId,
                    TextPrompt = pva.TextPrompt,
                    IsRequired = pva.IsRequired,
                    AttributeControlTypeId = pva.AttributeControlTypeId,
                    DisplayOrder = pva.DisplayOrder
                };

                _db.ProductVariantAttributes.Add(attributeClone);

                // Save associated value (used for combinations copying).
                attributeMap[pva.Id] = attributeClone;
            }

            // >>>>>> Commit attributes.
            await scope.CommitAsync();

            // Product variant attribute values.
            foreach (var attribute in product.ProductVariantAttributes)
            {
                var attributeClone = attributeMap[attribute.Id];
                foreach (var value in attribute.ProductVariantAttributeValues)
                {
                    var pvavClone = new ProductVariantAttributeValue
                    {
                        ProductVariantAttributeId = attributeClone.Id,
                        Name = value.Name,
                        Color = value.Color,
                        PriceAdjustment = value.PriceAdjustment,
                        WeightAdjustment = value.WeightAdjustment,
                        IsPreSelected = value.IsPreSelected,
                        DisplayOrder = value.DisplayOrder,
                        ValueTypeId = value.ValueTypeId,
                        LinkedProductId = value.LinkedProductId,
                        Quantity = value.Quantity,
                        MediaFileId = value.MediaFileId
                    };

                    _db.ProductVariantAttributeValues.Add(pvavClone);

                    // Save associated value (used for combinations copying).
                    valueMap.Add(value.Id, pvavClone);
                }
            }

            // >>>>>> Commit attribute values.
            await scope.CommitAsync();

            // Attribute value localization.
            foreach (var value in product.ProductVariantAttributes.SelectMany(x => x.ProductVariantAttributeValues).ToArray())
            {
                foreach (var lang in languages)
                {
                    string name = value.GetLocalized(x => x.Name, lang, false, false);
                    if (name.HasValue())
                    {
                        var pvavClone = valueMap.Get(value.Id);
                        if (pvavClone != null)
                        {
                            await _localizedEntityService.ApplyLocalizedValueAsync(pvavClone, x => x.Name, name, lang.Id);
                        }
                    }
                }
            }

            // >>>>>> Commit localized values.
            await scope.CommitAsync();

            // Attribute combinations.
            foreach (var combination in product.ProductVariantAttributeCombinations)
            {
                var oldAttributesMap = combination.AttributeSelection.AttributesMap;
                var oldAttributes = await _productAttributeMaterializer.MaterializeProductVariantAttributesAsync(combination.AttributeSelection);
                var newSelection = new ProductVariantAttributeSelection(null);

                foreach (var oldAttribute in oldAttributes)
                {
                    if (attributeMap.TryGetValue(oldAttribute.Id, out var newAttribute))
                    {
                        var item = oldAttributesMap.FirstOrDefault(x => x.Key == oldAttribute.Id);
                        if (item.Key != 0)
                        {
                            foreach (var value in item.Value)
                            {
                                if (newAttribute.IsListTypeAttribute())
                                {
                                    var oldValueId = value.ToString().EmptyNull().ToInt();
                                    if (valueMap.TryGetValue(oldValueId, out var newValue))
                                    {
                                        newSelection.AddAttributeValue(newAttribute.Id, newValue.Id);
                                    }
                                }
                                else
                                {
                                    newSelection.AddAttributeValue(newAttribute.Id, value);
                                }
                            }
                        }
                    }
                }

                var combinationClone = new ProductVariantAttributeCombination
                {
                    ProductId = clone.Id,
                    RawAttributes = newSelection.AsJson(),
                    StockQuantity = combination.StockQuantity,
                    AllowOutOfStockOrders = combination.AllowOutOfStockOrders,
                    Sku = combination.Sku,
                    Gtin = combination.Gtin,
                    ManufacturerPartNumber = combination.ManufacturerPartNumber,
                    Price = combination.Price,
                    AssignedMediaFileIds = combination.AssignedMediaFileIds,
                    Length = combination.Length,
                    Width = combination.Width,
                    Height = combination.Height,
                    BasePriceAmount = combination.BasePriceAmount,
                    BasePriceBaseAmount = combination.BasePriceBaseAmount,
                    DeliveryTimeId = combination.DeliveryTimeId,
                    QuantityUnitId = combination.QuantityUnitId,
                    IsActive = combination.IsActive
                    //IsDefaultCombination = combination.IsDefaultCombination
                };

                _db.ProductVariantAttributeCombinations.Add(combinationClone);
            }

            // >>>>>> Commit combinations.
            await scope.CommitAsync();
        }

        private async Task ProcessBundleItems(Product product, Product clone)
        {
            var bundledItems = await _db.ProductBundleItem
                .AsNoTracking()
                .Include(x => x.AttributeFilters)
                .ApplyBundledProductsFilter(new[] { product.Id }, true)
                .ToListAsync();

            foreach (var bundleItem in bundledItems)
            {
                var newBundleItem = bundleItem.Clone();
                newBundleItem.BundleProductId = clone.Id;

                _db.ProductBundleItem.Add(newBundleItem);

                foreach (var itemFilter in bundleItem.AttributeFilters)
                {
                    var newItemFilter = itemFilter.Clone();
                    newItemFilter.BundleItemId = newBundleItem.Id;

                    _db.ProductBundleItemAttributeFilter.Add(newItemFilter);
                }
            }
        }
        
        private async Task ProcessDownloads(Product product, Product clone)
        {
            var downloads = await _db.Downloads
                .AsNoTracking()
                .Include(x => x.MediaFile)
                .ApplyEntityFilter(nameof(Product), product.Id)
                .ToListAsync();

            foreach (var download in downloads)
            {
                var downloadClone = download.Clone();
                downloadClone.EntityId = clone.Id;

                _db.Downloads.Add(downloadClone);
            }
        }

        private async Task ProcessAssociatedProducts(Product product, Product clone, bool isPublished)
        {
            var associatedProducts = await _db.Products
                .ApplyAssociatedProductsFilter(new[] { product.Id }, true)
                .ToListAsync();

            foreach (var associatedProduct in associatedProducts)
            {
                var associatedProductClone = await CloneProductAsync(associatedProduct, T("Admin.Common.CopyOf", associatedProduct.Name), isPublished, false);
                associatedProductClone.ParentGroupedProductId = clone.Id;
            }
        }
    }
}