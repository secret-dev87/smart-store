﻿using Smartstore.ComponentModel;
using Smartstore.Core;
using Smartstore.Core.Catalog;
using Smartstore.Core.Catalog.Attributes;
using Smartstore.Core.Catalog.Pricing;
using Smartstore.Core.Checkout.Cart;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Smartstore.Web.Models.ShoppingCart
{
    public static partial class WishlistMappingExtensions
    {
        public static async Task MapAsync(this OrganizedShoppingCartItem entity, WishlistModel.WishlistItemModel model)
        {
            await MapperFactory.MapAsync(entity, model, null);
        }
    }

    public class WishlistItemModelMapper : CartItemMapperBase<WishlistModel.WishlistItemModel>
    {
        public WishlistItemModelMapper(
            ICommonServices services,
            IPriceCalculationService priceCalculationService,
            IProductAttributeMaterializer productAttributeMaterializer,
            ShoppingCartSettings shoppingCartSettings,
            CatalogSettings catalogSettings)
            : base(services, priceCalculationService, productAttributeMaterializer, shoppingCartSettings, catalogSettings)
        {
        }

        protected override void Map(OrganizedShoppingCartItem from, WishlistModel.WishlistItemModel to, dynamic parameters = null)
            => throw new NotImplementedException();

        public override async Task MapAsync(OrganizedShoppingCartItem from, WishlistModel.WishlistItemModel to, dynamic parameters = null)
        {
            Guard.NotNull(from, nameof(from));
            Guard.NotNull(to, nameof(to));

            await base.MapAsync(from, to);

            if (from.ChildItems != null)
            {
                foreach (var childItem in from.ChildItems.Where(x => x.Item.Id != from.Item.Id))
                {
                    var model = new WishlistModel.WishlistItemModel
                    {
                        DisableBuyButton = childItem.Item.Product.DisableBuyButton
                    };

                    await childItem.MapAsync(model);

                    to.AddChildItems(model);
                }
            }
        }
    }
}