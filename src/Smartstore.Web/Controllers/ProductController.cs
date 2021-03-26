﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Smartstore.Core.Catalog;
using Smartstore.Core.Catalog.Attributes;
using Smartstore.Core.Catalog.Products;
using Smartstore.Core.Catalog.Search;
using Smartstore.Core.Common.Settings;
using Smartstore.Core.Content.Media;
using Smartstore.Core.Content.Menus;
using Smartstore.Core.Data;
using Smartstore.Core.Identity;
using Smartstore.Core.Localization;
using Smartstore.Core.Messages;
using Smartstore.Core.Security;
using Smartstore.Core.Seo;
using Smartstore.Core.Stores;
using Smartstore.Utilities.Html;
using Smartstore.Web.Filters;
using Smartstore.Web.Models.Catalog;

namespace Smartstore.Web.Controllers
{
    public partial class ProductController : PublicControllerBase
    {
        private readonly SmartDbContext _db;
        private readonly IProductService _productService;
        private readonly IProductTagService _productTagService;
        private readonly IProductAttributeService _productAttributeService;
        private readonly IRecentlyViewedProductsService _recentlyViewedProductsService;
        private readonly IAclService _aclService;
        private readonly IStoreMappingService _storeMappingService;
        private readonly ICatalogSearchService _catalogSearchService;
        private readonly IMediaService _mediaService;
        private readonly MediaSettings _mediaSettings;
        private readonly CatalogSettings _catalogSettings;
        private readonly IProductCompareService _productCompareService;
        private readonly CatalogHelper _helper;
        private readonly IBreadcrumb _breadcrumb;
        private readonly SeoSettings _seoSettings;
        private readonly ContactDataSettings _contactDataSettings;
        private readonly CaptchaSettings _captchaSettings;
        private readonly Lazy<IUrlHelper> _urlHelper;
        private readonly Lazy<IMessageFactory> _messageFactory;

        public ProductController(
            SmartDbContext db,
            IProductService productService,
            IProductTagService productTagService,
            IProductAttributeService productAttributeService,
            IRecentlyViewedProductsService recentlyViewedProductsService,
            IProductCompareService productCompareService,
            IAclService aclService,
            IStoreMappingService storeMappingService,
            ICatalogSearchService catalogSearchService,
            IMediaService mediaService,
            MediaSettings mediaSettings,
            CatalogSettings catalogSettings,
            CatalogHelper helper,
            IBreadcrumb breadcrumb,
            SeoSettings seoSettings,
            ContactDataSettings contactDataSettings,
            CaptchaSettings captchaSettings,
            Lazy<IUrlHelper> urlHelper,
            Lazy<IMessageFactory> messageFactory)
        {
            _db = db;
            _productService = productService;
            _productTagService = productTagService;
            _productAttributeService = productAttributeService;
            _recentlyViewedProductsService = recentlyViewedProductsService;
            _productCompareService = productCompareService;
            _aclService = aclService;
            _storeMappingService = storeMappingService;
            _catalogSearchService = catalogSearchService;
            _mediaService = mediaService;
            _mediaSettings = mediaSettings;
            _catalogSettings = catalogSettings;
            _helper = helper;
            _breadcrumb = breadcrumb;
            _seoSettings = seoSettings;
            _contactDataSettings = contactDataSettings;
            _captchaSettings = captchaSettings;
            _urlHelper = urlHelper;
            _messageFactory = messageFactory;
        }

        #region Products

        public async Task<IActionResult> ProductDetails(int productId, ProductVariantQuery query)
        {
            var product = await _db.Products
                .IncludeMedia()
                .IncludeManufacturers()
                .IncludeBundleItems()
                .Where(x => x.Id == productId)
                .FirstOrDefaultAsync();

            if (product == null || product.Deleted || product.IsSystemProduct)
                return NotFound();

            // Is published? Check whether the current user has a "Manage catalog" permission.
            // It allows him to preview a product before publishing.
            if (!product.Published && !await Services.Permissions.AuthorizeAsync(Permissions.Catalog.Product.Read))
                return NotFound();

            // ACL (access control list).
            if (!await _aclService.AuthorizeAsync(product))
                return NotFound();

            // Store mapping.
            if (!await _storeMappingService.AuthorizeAsync(product))
                return NotFound();

            // Is product individually visible?
            if (product.Visibility == ProductVisibility.Hidden)
            {
                // Find parent grouped product.
                var parentGroupedProduct = await _db.Products.FindByIdAsync(product.ParentGroupedProductId, false);
                if (parentGroupedProduct == null)
                    return NotFound();

                var seName = await parentGroupedProduct.GetActiveSlugAsync();
                if (seName.IsEmpty())
                    return NotFound();

                var routeValues = new RouteValueDictionary
                {
                    { "SeName", seName }
                };

                // Add query string parameters.
                Request.Query.Each(x => routeValues.Add(x.Key, Request.Query[x.Value].ToString()));

                return RedirectToRoute("Product", routeValues);
            }

            // Prepare the view model
            var model = await _helper.PrepareProductDetailsPageModelAsync(product, query);

            // Some cargo data
            model.PictureSize = _mediaSettings.ProductDetailsPictureSize;
            model.HotlineTelephoneNumber = _contactDataSettings.HotlineTelephoneNumber.NullEmpty();
            if (_seoSettings.CanonicalUrlsEnabled)
            {
                model.CanonicalUrl = _urlHelper.Value.RouteUrl("Product", new { model.SeName }, Request.Scheme);
            }

            // Save as recently viewed
            _recentlyViewedProductsService.AddProductToRecentlyViewedList(product.Id);

            // Activity log
            Services.ActivityLogger.LogActivity("PublicStore.ViewProduct", T("ActivityLog.PublicStore.ViewProduct"), product.Name);

            // Breadcrumb
            if (_catalogSettings.CategoryBreadcrumbEnabled)
            {
                await _helper.GetBreadcrumbAsync(_breadcrumb, ControllerContext, product);

                _breadcrumb.Track(new MenuItem
                {
                    Text = model.Name,
                    Rtl = model.Name.CurrentLanguage.Rtl,
                    EntityId = product.Id,
                    Url = Url.RouteUrl("Product", new { model.SeName })
                });
            }

            return View(model.ProductTemplateViewPath, model);
        }

        /// <summary>
        /// TODO: (mh) (core) Describe what this action does.
        /// </summary>
        [HttpPost]
        public async Task<ActionResult> UpdateProductDetails(int productId, string itemType, int bundleItemId, ProductVariantQuery query)
        {
            // TODO: (core) UpdateProductDetails action needs some decent refactoring.
            var form = HttpContext.Request.Form;
            int quantity = 1;
            int galleryStartIndex = -1;
            string galleryHtml = null;
            string dynamicThumbUrl = null;
            var isAssociated = itemType.EqualsNoCase("associateditem");
            var m = new ProductDetailsModel();
            var product = await _db.Products.FindByIdAsync(productId);
            var bItem = await _db.ProductBundleItem.FindByIdAsync(bundleItemId);
            IList<ProductBundleItemData> bundleItems = null;
            ProductBundleItemData bundleItem = bItem == null ? null : new ProductBundleItemData(bItem);
            
            // Quantity required for tier prices.
            string quantityKey = form.Keys.FirstOrDefault(k => k.EndsWith("EnteredQuantity"));
            if (quantityKey.HasValue())
            {
                _ = int.TryParse(form[quantityKey], out quantity);
            }

            if (product.ProductType == ProductType.BundledProduct && product.BundlePerItemPricing)
            {
                var bundleItemQuery = await _db.ProductBundleItem
                    .AsNoTracking()
                    .ApplyBundledProductsFilter(new[] { product.Id })
                    .Include(x => x.Product)
                    .Include(x => x.BundleProduct)
                    .ToListAsync();

                if (bundleItemQuery.Count > 0)
                {
                    bundleItems = new List<ProductBundleItemData>();
                    bundleItemQuery.Each(x => bundleItems.Add(new ProductBundleItemData(x)));
                }

                if (query.Variants.Count > 0)
                {
                    // May add elements to query object if they are preselected by bundle item filter.
                    foreach (var itemData in bundleItems)
                    {
                        await _helper.PrepareProductDetailsPageModelAsync(itemData.Item.Product, query, false, itemData);
                    }
                }
            }

            // Get merged model data.
            await _helper.PrepareProductDetailModelAsync(m, product, query, isAssociated, bundleItem, bundleItems, quantity);

            if (bundleItem != null)
            {
                // Update bundle item thumbnail.
                if (!bundleItem.Item.HideThumbnail)
                {
                    var assignedMediaIds = m.SelectedCombination?.GetAssignedMediaIds() ?? Array.Empty<int>();
                    var hasFile = await _db.MediaFiles.AnyAsync(x => x.Id == assignedMediaIds[0]);
                    if (assignedMediaIds.Any() && hasFile)
                    {
                        var file = await _db.ProductMediaFiles
                            .AsNoTracking()
                            .ApplyProductFilter(new[] { bundleItem.Item.ProductId }, 1)
                            .Select(x => x.MediaFile)
                            .FirstOrDefaultAsync();

                        dynamicThumbUrl = _mediaService.GetUrl(file, _mediaSettings.BundledProductPictureSize, null, false);
                    }
                }
            }
            else if (isAssociated)
            {
                // Update associated product thumbnail.
                var assignedMediaIds = m.SelectedCombination?.GetAssignedMediaIds() ?? new int[0];
                var hasFile = await _db.MediaFiles.AnyAsync(x => x.Id == assignedMediaIds[0]);
                if (assignedMediaIds.Any() && hasFile)
                {
                    var file = await _db.ProductMediaFiles
                        .AsNoTracking()
                        .ApplyProductFilter(new[] { productId }, 1)
                        .Select(x => x.MediaFile)
                        .FirstOrDefaultAsync();

                    dynamicThumbUrl = _mediaService.GetUrl(file, _mediaSettings.AssociatedProductPictureSize, null, false);
                }
            }
            else if (product.ProductType != ProductType.BundledProduct)
            {
                // Update image gallery.
                var files = await _db.ProductMediaFiles
                    .AsNoTracking()
                    .ApplyProductFilter(new[] { productId })
                    .Select(x => _mediaService.ConvertMediaFile(x.MediaFile))
                    .ToListAsync();

                if (product.HasPreviewPicture && files.Count > 1)
                {
                    files.RemoveAt(0);
                }

                if (files.Count <= _catalogSettings.DisplayAllImagesNumber)
                {
                    // All pictures rendered... only index is required.
                    galleryStartIndex = 0;

                    var assignedMediaIds = m.SelectedCombination?.GetAssignedMediaIds() ?? new int[0];
                    if (assignedMediaIds.Any())
                    {
                        var file = files.FirstOrDefault(p => p.Id == assignedMediaIds[0]);
                        galleryStartIndex = file == null ? 0 : files.IndexOf(file);
                    }
                }
                else
                {
                    var allCombinationPictureIds = await _productAttributeService.GetAttributeCombinationFileIdsAsync(product.Id);

                    var mediaModel = _helper.PrepareProductDetailsMediaGalleryModel(
                        files,
                        product.GetLocalized(x => x.Name),
                        allCombinationPictureIds,
                        false,
                        bundleItem,
                        m.SelectedCombination);

                    galleryStartIndex = mediaModel.GalleryStartIndex;
                    galleryHtml = (await this.InvokeViewAsync("Product.Media", mediaModel)).ToString();
                }

                m.PriceDisplayStyle = _catalogSettings.PriceDisplayStyle;
                m.DisplayTextForZeroPrices = _catalogSettings.DisplayTextForZeroPrices;
            }

            object partials = null;

            if (m.IsBundlePart)
            {
                partials = new
                {
                    BundleItemPrice = await this.InvokeViewAsync("Product.Offer.Price", m),
                    BundleItemStock = await this.InvokeViewAsync("Product.StockInfo", m),
                    BundleItemVariants = await this.InvokeViewAsync("Product.Variants", m.ProductVariantAttributes)
                };
            }
            else
            {
                var dataDictAddToCart = new ViewDataDictionary(ViewData);
                dataDictAddToCart.TemplateInfo.HtmlFieldPrefix = $"addtocart_{m.Id}";

                decimal adjustment = decimal.Zero;
                decimal taxRate = decimal.Zero;

                // TODO: (mh) (core) Implement when pricing is available.
                //var finalPriceWithDiscountBase = _taxService.GetProductPrice(product, product.Price, Services.WorkContext.CurrentCustomer, out taxRate);

                //if (!_taxSettings.Value.PricesIncludeTax && Services.WorkContext.TaxDisplayType == TaxDisplayType.IncludingTax)
                //{
                //    adjustment = (m.ProductPrice.PriceValue - finalPriceWithDiscountBase) / (taxRate / 100 + 1);
                //}
                //else if (_taxSettings.Value.PricesIncludeTax && Services.WorkContext.TaxDisplayType == TaxDisplayType.ExcludingTax)
                //{
                //    adjustment = (m.ProductPrice.PriceValue - finalPriceWithDiscountBase) * (taxRate / 100 + 1);
                //}
                //else
                //{
                //    adjustment = m.ProductPrice.PriceValue - finalPriceWithDiscountBase;
                //}

                partials = new
                {
                    Attrs = await this.InvokeViewAsync("Product.Attrs", m),
                    Price = await this.InvokeViewAsync("Product.Offer.Price", m),
                    Stock = await this.InvokeViewAsync("Product.StockInfo", m),
                    Variants = await this.InvokeViewAsync("Product.Variants", m.ProductVariantAttributes),

                    // TODO: (mc) (core) We may need another parameter for this.
                    //OfferActions = await _razorViewInvoker.Value.InvokeViewAsync("Product.Offer.Actions", m, dataDictAddToCart),
                    
                    // TODO: (mh) (core) Implement when Component or Partial is available.
                    //TierPrices = await _razorViewInvoker.Value.InvokeViewAsync("Product.TierPrices", await _razorViewInvoker.InvokeViewAsync(product, adjustment)),
                    BundlePrice = product.ProductType == ProductType.BundledProduct ? await this.InvokeViewAsync("Product.Bundle.Price", m) : null
                };
            }

            object data = new
            {
                Partials = partials,
                DynamicThumblUrl = dynamicThumbUrl,
                GalleryStartIndex = galleryStartIndex,
                GalleryHtml = galleryHtml
            };

            return new JsonResult(new { Data = data });
        }

        #endregion

        #region Email a friend

        [GdprConsent]
        public async Task<IActionResult> EmailAFriend(int id)
        {
            var product = await _db.Products.FindByIdAsync(id, false);

            if (product == null || product.Deleted || product.IsSystemProduct || !product.Published || !_catalogSettings.EmailAFriendEnabled)
                return NotFound();

            var model = await PrepareEmailAFriendModelAsync(product);

            return View(model);
        }

        [HttpPost, ActionName("EmailAFriend")]
        [ValidateCaptcha]
        [GdprConsent]
        public async Task<IActionResult> EmailAFriendSend(ProductEmailAFriendModel model, int id, string captchaError)
        {
            var product = await _db.Products.FindByIdAsync(id, false);
            if (product == null || product.Deleted || product.IsSystemProduct || !product.Published || !_catalogSettings.EmailAFriendEnabled)
                return NotFound();

            if (_captchaSettings.ShowOnEmailProductToFriendPage && captchaError.HasValue())
            {
                ModelState.AddModelError("", captchaError);
            }

            var customer = Services.WorkContext.CurrentCustomer;

            // Check whether the current customer is guest and ia allowed to email a friend.
            if (customer.IsGuest() && !_catalogSettings.AllowAnonymousUsersToEmailAFriend)
            {
                ModelState.AddModelError("", T("Products.EmailAFriend.OnlyRegisteredUsers"));
            }

            if (ModelState.IsValid)
            {
                //email
                await _messageFactory.Value.SendShareProductMessageAsync(
                    customer,
                    product,
                    model.YourEmailAddress,
                    model.FriendEmail,
                    HtmlUtils.ConvertPlainTextToHtml(model.PersonalMessage.HtmlEncode()));

                NotifySuccess(T("Products.EmailAFriend.SuccessfullySent"));

                return RedirectToRoute("Product", new { SeName = await product.GetActiveSlugAsync() });
            }

            // If we got this far something failed. Redisplay form.
            model = await PrepareEmailAFriendModelAsync(product);

            return View(model);
        }

        private async Task<ProductEmailAFriendModel> PrepareEmailAFriendModelAsync(Product product)
        {
            var model = new ProductEmailAFriendModel
            {
                ProductId = product.Id,
                ProductName = product.GetLocalized(x => x.Name),
                ProductSeName = await product.GetActiveSlugAsync(),
                YourEmailAddress = Services.WorkContext.CurrentCustomer.Email,
                AllowChangedCustomerEmail = _catalogSettings.AllowDifferingEmailAddressForEmailAFriend,
                DisplayCaptcha = _captchaSettings.CanDisplayCaptcha && _captchaSettings.ShowOnEmailProductToFriendPage
            };

            return model;
        }

        #endregion

    }
}

