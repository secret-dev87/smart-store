﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Dynamic.Core;
using System.Threading.Tasks;
using Dasync.Collections;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Smartstore.Admin.Models.Catalog;
using Smartstore.ComponentModel;
using Smartstore.Core.Catalog.Attributes;
using Smartstore.Core.Catalog.Products;
using Smartstore.Core.Catalog.Search;
using Smartstore.Core.Rules.Filters;
using Smartstore.Core.Security;
using Smartstore.Data.Batching;
using Smartstore.Web.Controllers;
using Smartstore.Web.Modelling;
using Smartstore.Web.Modelling.DataGrid;

namespace Smartstore.Admin.Controllers
{
    public partial class ProductController : AdminControllerBase
    {
        #region Related products

        [HttpPost]
        [Permission(Permissions.Catalog.Product.Read)]
        public async Task<IActionResult> RelatedProductList(GridCommand command, int productId)
        {
            var model = new GridModel<ProductModel.RelatedProductModel>();
            var relatedProducts = await _db.RelatedProducts
                .AsNoTracking()
                .ApplyProductId1Filter(productId)
                .ApplyGridCommand(command)
                .ToListAsync();

            var relatedProductsModel = await relatedProducts
                .SelectAsync(async x =>
                {
                    // TODO: (mh) (core) (perf) Load all products in one go (projected)
                    var product2 = await _db.Products.FindByIdAsync(x.ProductId2, false);

                    return new ProductModel.RelatedProductModel()
                    {
                        Id = x.Id,
                        ProductId2 = x.ProductId2,
                        Product2Name = product2.Name,
                        ProductTypeName = product2.GetProductTypeLabel(Services.Localization),
                        ProductTypeLabelHint = product2.ProductTypeLabelHint,
                        DisplayOrder = x.DisplayOrder,
                        Product2Sku = product2.Sku,
                        Product2Published = product2.Published,
                        EditUrl = Url.Action("Edit", "Product", new { id = product2.Id })
                    };
                })
                .AsyncToList();

            model.Rows = relatedProductsModel;
            model.Total = relatedProductsModel.Count;

            return Json(model);
        }

        [HttpPost]
        [Permission(Permissions.Catalog.Product.EditPromotion)]
        public async Task<IActionResult> RelatedProductUpdate(ProductModel.RelatedProductModel model)
        {
            var relatedProduct = await _db.RelatedProducts.FindByIdAsync(model.Id);
            relatedProduct.DisplayOrder = model.DisplayOrder;

            try
            {
                await _db.SaveChangesAsync();
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                NotifyError(ex.GetInnerMessage());
                return Json(new { success = false });
            }
        }

        [HttpPost]
        [Permission(Permissions.Catalog.Product.EditCategory)]
        public async Task<IActionResult> RelatedProductDelete(GridSelection selection)
        {
            var ids = selection.GetEntityIds();
            var numDeleted = 0;

            if (ids.Any())
            {
                var toDelete = await _db.RelatedProducts
                    .AsQueryable()
                    .Where(x => ids.Contains(x.Id))
                    .ToListAsync();

                numDeleted = toDelete.Count;

                _db.RelatedProducts.RemoveRange(toDelete);
                await _db.SaveChangesAsync();
            }

            return Json(new { Success = true, Count = numDeleted });
        }

        [HttpPost]
        [Permission(Permissions.Catalog.Product.EditPromotion)]
        public async Task<IActionResult> RelatedProductAdd(int productId, int[] selectedProductIds)
        {
            var products = await _db.Products
                .AsNoTracking()
                .Where(x => selectedProductIds.Contains(x.Id))
                .ApplyStandardFilter()
                .ToListAsync();

            RelatedProduct relation = null;
            var maxDisplayOrder = -1;

            foreach (var product in products)
            {
                // TODO: (mh) (core) (perf) Load all products to a multimap in one go
                var existingRelations = await _db.RelatedProducts
                    .ApplyProductId1Filter(productId)
                    .ToListAsync();

                if (FindRelatedProduct(existingRelations, productId, product.Id) == null)
                {
                    if (maxDisplayOrder == -1 && (relation = existingRelations.OrderByDescending(x => x.DisplayOrder).FirstOrDefault()) != null)
                    {
                        maxDisplayOrder = relation.DisplayOrder;
                    }

                    _db.RelatedProducts.Add(new RelatedProduct
                    {
                        ProductId1 = productId,
                        ProductId2 = product.Id,
                        DisplayOrder = ++maxDisplayOrder
                    });
                }
            }

            await _db.SaveChangesAsync();

            return new EmptyResult();
        }

        [HttpPost]
        [Permission(Permissions.Catalog.Product.EditPromotion)]
        public async Task<IActionResult> CreateAllMutuallyRelatedProducts(int productId)
        {
            string message = null;
            var product = await _db.Products.FindByIdAsync(productId, false);

            if (product != null)
            {
                var count = await _productService.EnsureMutuallyRelatedProductsAsync(productId);
                message = T("Admin.Common.CreateMutuallyAssociationsResult", count);
            }

            return new JsonResult(new { Message = message });
        }

        /// <summary>
        /// Finds a related product item by specified identifiers
        /// </summary>
        /// <param name="source">Source</param>
        /// <param name="productId1">The first product identifier</param>
        /// <param name="productId2">The second product identifier</param>
        /// <returns>Related product</returns>
        private static RelatedProduct FindRelatedProduct(List<RelatedProduct> source, int productId1, int productId2)
        {
            return source.Where(x => x.ProductId1 == productId1 && x.ProductId2 == productId2).FirstOrDefault();
        }

        #endregion

        #region Cross-sell products

        [HttpPost]
        [Permission(Permissions.Catalog.Product.Read)]
        public async Task<IActionResult> CrossSellProductList(GridCommand command, int productId)
        {
            var model = new GridModel<ProductModel.CrossSellProductModel>();
            var crossSellProducts = await _db.CrossSellProducts
                .AsNoTracking()
                .ApplyProductId1Filter(productId, true)
                .ApplyGridCommand(command)
                .ToListAsync();

            var crossSellProductsModel = await crossSellProducts
                .SelectAsync(async x =>
                {
                    var product2 = await _db.Products.FindByIdAsync(x.ProductId2, false);

                    return new ProductModel.CrossSellProductModel
                    {
                        Id = x.Id,
                        ProductId2 = x.ProductId2,
                        Product2Name = product2.Name,
                        ProductTypeName = product2.GetProductTypeLabel(Services.Localization),
                        ProductTypeLabelHint = product2.ProductTypeLabelHint,
                        Product2Sku = product2.Sku,
                        Product2Published = product2.Published,
                        EditUrl = Url.Action("Edit", "Product", new { id = product2.Id })
                    };
                })
                .ToListAsync();

            model.Rows = crossSellProductsModel;
            model.Total = crossSellProductsModel.Count;

            return Json(model);
        }

        [Permission(Permissions.Catalog.Product.EditPromotion)]
        public async Task<IActionResult> CrossSellProductDelete(GridSelection selection)
        {
            var ids = selection.GetEntityIds();
            var numDeleted = 0;

            if (ids.Any())
            {
                var toDelete = await _db.CrossSellProducts
                    .AsQueryable()
                    .Where(x => ids.Contains(x.Id))
                    .ToListAsync();

                numDeleted = toDelete.Count;

                _db.CrossSellProducts.RemoveRange(toDelete);
                await _db.SaveChangesAsync();
            }

            return Json(new { Success = true, Count = numDeleted });
        }

        [HttpPost]
        [Permission(Permissions.Catalog.Product.EditPromotion)]
        public async Task<IActionResult> CrossSellProductAdd(int productId, int[] selectedProductIds)
        {
            var products = await _db.Products
                .AsNoTracking()
                .Where(x => selectedProductIds.Contains(x.Id))
                .ToListAsync();

            foreach (var product in products.OrderBySequence(selectedProductIds))
            {
                var existingRelations = await _db.CrossSellProducts
                    .ApplyProductId1Filter(productId)
                    .ToListAsync();

                if (FindCrossSellProduct(existingRelations, productId, product.Id) == null)
                {
                    _db.CrossSellProducts.Add(new CrossSellProduct
                    {
                        ProductId1 = productId,
                        ProductId2 = product.Id
                    });
                }
            }

            await _db.SaveChangesAsync();

            return new EmptyResult();
        }

        [HttpPost]
        [Permission(Permissions.Catalog.Product.EditPromotion)]
        public async Task<IActionResult> CreateAllMutuallyCrossSellProducts(int productId)
        {
            string message = null;
            var product = await _db.Products.FindByIdAsync(productId, false);
            if (product != null)
            {
                var count = await _productService.EnsureMutuallyCrossSellProductsAsync(productId);
                message = T("Admin.Common.CreateMutuallyAssociationsResult", count);
            }

            return new JsonResult(new { Message = message });
        }

        /// <summary>
        /// Finds a cross-sell product item by specified identifiers
        /// </summary>
        /// <param name="source">Source</param>
        /// <param name="productId1">The first product identifier</param>
        /// <param name="productId2">The second product identifier</param>
        /// <returns>Cross-sell product</returns>
        public static CrossSellProduct FindCrossSellProduct(List<CrossSellProduct> source, int productId1, int productId2)
        {
            return source.Where(x => x.ProductId1 == productId1 && x.ProductId2 == productId2).FirstOrDefault();
        }

        #endregion

        #region Associated products

        [HttpPost]
        [Permission(Permissions.Catalog.Product.Read)]
        public async Task<IActionResult> AssociatedProductList(GridCommand command, int productId)
        {
            var model = new GridModel<ProductModel.AssociatedProductModel>();
            var searchQuery = new CatalogSearchQuery().HasParentGroupedProduct(productId);
            var query = _catalogSearchService.Value.PrepareQuery(searchQuery);
            var associatedProducts = await query
                .OrderBy(p => p.DisplayOrder)
                .ApplyGridCommand(command)
                .ToListAsync();

            var associatedProductsModel = associatedProducts.Select(x =>
            {
                return new ProductModel.AssociatedProductModel
                {
                    Id = x.Id,
                    Name = x.Name,
                    ProductTypeName = x.GetProductTypeLabel(Services.Localization),
                    ProductTypeLabelHint = x.ProductTypeLabelHint,
                    DisplayOrder = x.DisplayOrder,
                    Sku = x.Sku,
                    Published = x.Published,
                    EditUrl = Url.Action("Edit", "Product", new { id = x.Id })
                };
            })
            .ToList();

            model.Rows = associatedProductsModel;
            model.Total = associatedProductsModel.Count;

            return Json(model);
        }

        [HttpPost]
        [Permission(Permissions.Catalog.Product.EditAssociatedProduct)]
        public async Task<IActionResult> AssociatedProductUpdate(ProductModel.RelatedProductModel model)
        {
            var relatedProduct = await _db.Products.FindByIdAsync(model.Id);
            relatedProduct.DisplayOrder = model.DisplayOrder;

            try
            {
                await _db.SaveChangesAsync();
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                NotifyError(ex.GetInnerMessage());
                return Json(new { success = false });
            }
        }

        [Permission(Permissions.Catalog.Product.EditAssociatedProduct)]
        public async Task<IActionResult> AssociatedProductDelete(GridSelection selection)
        {
            var ids = selection.GetEntityIds();
            var numDeleted = 0;

            if (ids.Any())
            {
                var products = await _db.Products
                    .AsQueryable()
                    .Where(x => ids.Contains(x.Id))
                    .ToListAsync();

                foreach (var product in products)
                {
                    product.ParentGroupedProductId = 0;
                    numDeleted++;
                }

                await _db.SaveChangesAsync();
            }

            return Json(new { Success = true, Count = numDeleted });
        }

        [HttpPost]
        [Permission(Permissions.Catalog.Product.EditAssociatedProduct)]
        public async Task<IActionResult> AssociatedProductAdd(int productId, int[] selectedProductIds)
        {
            var searchQuery = new CatalogSearchQuery().HasParentGroupedProduct(productId);
            var query = _catalogSearchService.Value.PrepareQuery(searchQuery);
            var maxDisplayOrder = query
                .Select(x => x.DisplayOrder)
                .OrderByDescending(x => x)
                .FirstOrDefault();

            var products = await _db.Products
                .AsQueryable()
                .Where(x => selectedProductIds.Contains(x.Id))
                .ToListAsync();

            foreach (var product in products)
            {
                if (product.ParentGroupedProductId != productId)
                {
                    product.ParentGroupedProductId = productId;
                    product.DisplayOrder = ++maxDisplayOrder;
                }
            }

            await _db.SaveChangesAsync();

            return new EmptyResult();
        }

        #endregion

        #region Bundle items

        [HttpPost]
        [Permission(Permissions.Catalog.Product.Read)]
        public async Task<IActionResult> BundleItemList(GridCommand command, int productId)
        {
            var model = new GridModel<ProductModel.BundleItemModel>();
            var bundleItems = await _db.ProductBundleItem
                .AsNoTracking()
                .ApplyBundledProductsFilter(new[] { productId }, true)
                .Include(x => x.Product)
                .ApplyGridCommand(command)
                .ToListAsync();

            var bundleItemsModel = bundleItems.Select(x =>
            {
                return new ProductModel.BundleItemModel
                {
                    Id = x.Id,
                    ProductId = x.Product.Id,
                    ProductName = x.Product.Name,
                    ProductTypeName = x.Product.GetProductTypeLabel(Services.Localization),
                    ProductTypeLabelHint = x.Product.ProductTypeLabelHint,
                    Sku = x.Product.Sku,
                    Quantity = x.Quantity,
                    Discount = x.Discount,
                    DisplayOrder = x.DisplayOrder,
                    Visible = x.Visible,
                    Published = x.Published
                };
            }).ToList();

            model.Rows = bundleItemsModel;
            model.Total = bundleItemsModel.Count;

            return Json(model);
        }

        [Permission(Permissions.Catalog.Product.EditBundle)]
        public async Task<IActionResult> BundleItemDelete(GridSelection selection)
        {
            var ids = selection.GetEntityIds();
            var numDeleted = 0;

            if (ids.Any())
            {
                var toDelete = await _db.ProductBundleItem
                    .AsQueryable()
                    .Where(x => ids.Contains(x.Id))
                    .ToListAsync();

                numDeleted = toDelete.Count;

                _db.ProductBundleItem.RemoveRange(toDelete);
                await _db.SaveChangesAsync();
            }

            return Json(new { Success = true, Count = numDeleted });
        }

        [HttpPost]
        [Permission(Permissions.Catalog.Product.EditBundle)]
        public async Task<IActionResult> BundleItemAdd(int productId, int[] selectedProductIds)
        {
            var utcNow = DateTime.UtcNow;
            var products = await _db.Products.GetManyAsync(selectedProductIds, true);

            var maxDisplayOrder = await _db.ProductBundleItem
                .AsNoTracking()
                .ApplyBundledProductsFilter(new[] { productId }, true)
                .OrderByDescending(x => x.DisplayOrder)
                .Select(x => x.DisplayOrder)
                .FirstOrDefaultAsync();

            // TODO: (mh) (core) No notification if not CanBeBundleItem() :-/ > Fix that!
            foreach (var product in products.Where(x => x.CanBeBundleItem()))
            {
                var attributes = await _db.ProductVariantAttributes
                    .ApplyProductFilter(new[] { product.Id })
                    .ToListAsync();

                if (attributes.Count > 0 && attributes.Any(a => a.ProductVariantAttributeValues.Any(v => v.ValueType == ProductVariantAttributeValueType.ProductLinkage)))
                {
                    NotifyError(T("Admin.Catalog.Products.BundleItems.NoAttributeWithProductLinkage"));
                }
                else
                {
                    var bundleItem = new ProductBundleItem
                    {
                        ProductId = product.Id,
                        BundleProductId = productId,
                        Quantity = 1,
                        Visible = true,
                        Published = true,
                        DisplayOrder = ++maxDisplayOrder
                    };

                    _db.ProductBundleItem.Add(bundleItem);
                }
            }

            await _db.SaveChangesAsync();

            return new EmptyResult();
        }

        [Permission(Permissions.Catalog.Product.Read)]
        public async Task<IActionResult> BundleItemEditPopup(int id, string btnId, string formId)
        {
            var bundleItem = await _db.ProductBundleItem
                .Include(x => x.BundleProduct)
                .Include(x => x.Product)
                .FindByIdAsync(id, false);

            if (bundleItem == null)
            {
                throw new ArgumentException("No bundle item found with the specified id");
            }

            var model = await MapperFactory.MapAsync<ProductBundleItem, ProductBundleItemModel>(bundleItem);
            await PrepareBundleItemEditModelAsync(model, bundleItem, btnId, formId);

            return View(model);
        }

        [HttpPost, ParameterBasedOnFormName("save-continue", "continueEditing")]
        [Permission(Permissions.Catalog.Product.EditBundle)]
        public async Task<IActionResult> BundleItemEditPopup(string btnId, string formId, bool continueEditing, ProductBundleItemModel model)
        {
            ViewBag.CloseWindow = !continueEditing;

            if (ModelState.IsValid)
            {
                var bundleItem = await _db.ProductBundleItem
                    .Include(x => x.BundleProduct)
                    .Include(x => x.Product)
                    .FindByIdAsync(model.Id);

                if (bundleItem == null)
                {
                    throw new ArgumentException("No bundle item found with the specified id");
                }

                await MapperFactory.MapAsync(model, bundleItem);
                await _db.SaveChangesAsync();

                foreach (var localized in model.Locales)
                {
                    await _localizedEntityService.ApplyLocalizedValueAsync(bundleItem, x => x.Name, localized.Name, localized.LanguageId);
                    await _localizedEntityService.ApplyLocalizedValueAsync(bundleItem, x => x.ShortDescription, localized.ShortDescription, localized.LanguageId);
                }

                if (bundleItem.FilterAttributes)
                {
                    // Only update filters if attribute filtering is activated to reduce payload.
                    await SaveFilteredAttributesAsync(bundleItem);
                }

                await PrepareBundleItemEditModelAsync(model, bundleItem, btnId, formId, true);

                if (continueEditing)
                {
                    NotifySuccess(T("Admin.Common.DataSuccessfullySaved"));
                }
            }
            else
            {
                await PrepareBundleItemEditModelAsync(model, null, btnId, formId);
            }

            return View(model);
        }

        #endregion
    }
}
