﻿using Smartstore.Core;
using Smartstore.Core.Catalog.Pricing;
using Smartstore.Core.Catalog.Products;
using Smartstore.Core.Checkout.Cart;
using Smartstore.Core.Checkout.Orders;
using Smartstore.Core.Checkout.Payment;
using Smartstore.Core.Checkout.Tax;
using Smartstore.Core.Common.Services;
using Smartstore.Core.Localization;
using Smartstore.StripeElements.Models;
using Smartstore.StripeElements.Providers;

namespace Smartstore.StripeElements.Services
{
    public class StripeHelper
    {
        private readonly ICommonServices _services;
        private readonly IShoppingCartService _shoppingCartService;
        private readonly ITaxService _taxService;
        private readonly IPriceCalculationService _priceCalculationService;
        private readonly IProductService _productService;
        private readonly IOrderCalculationService _orderCalculationService;
        private readonly ICurrencyService _currencyService;
        private readonly IPaymentService _paymentService;

        public StripeHelper(
            ICommonServices services,
            IShoppingCartService shoppingCartService,
            ITaxService taxService,
            IPriceCalculationService priceCalculationService,
            IProductService productService,
            IOrderCalculationService orderCalculationService,
            ICurrencyService currencyService,
            IPaymentService paymentService)
        {
            _services = services;
            _shoppingCartService = shoppingCartService;
            _taxService = taxService;
            _priceCalculationService = priceCalculationService;
            _productService = productService;
            _orderCalculationService = orderCalculationService;
            _currencyService = currencyService;
            _paymentService = paymentService;
        }

        public Localizer T { get; set; } = NullLocalizer.Instance;

        public async Task<StripePaymentRequest> GetStripePaymentRequestAsync()
        {
            var store = _services.StoreContext.CurrentStore;
            var customer = _services.WorkContext.CurrentCustomer;
            var currency = _services.WorkContext.WorkingCurrency;
            var cart = await _shoppingCartService.GetCartAsync(customer, ShoppingCartType.ShoppingCart, store.Id);

            // Get subtotal
            var cartSubTotal = await _orderCalculationService.GetShoppingCartSubtotalAsync(cart, true);
            var subTotalConverted = _currencyService.ConvertFromPrimaryCurrency(cartSubTotal.SubtotalWithoutDiscount.Amount, currency);

            var cartProducts = cart.Items.Select(x => x.Item.Product).ToArray();
            var batchContext = _productService.CreateProductBatchContext(cartProducts, null, customer, false);
            var calculationOptions = _priceCalculationService.CreateDefaultOptions(false, customer, currency, batchContext);

            var displayItems = new List<StripePaymentItem>();

            foreach (var item in cart.Items)
            {
                var taxRate = await _taxService.GetTaxRateAsync(item.Item.Product);
                var calculationContext = await _priceCalculationService.CreateCalculationContextAsync(item, calculationOptions);
                var (unitPrice, subtotal) = await _priceCalculationService.CalculateSubtotalAsync(calculationContext);

                displayItems.Add(new StripePaymentItem
                {
                    Label = item.Item.Product.GetLocalized(x => x.Name),
                    Amount = subtotal.FinalPrice.Amount.ToSmallestCurrencyUnit(),
                    Pending = false
                });
            }

            // Prepare Stripe payment request object.
            var stripePaymentRequest = new StripePaymentRequest
            {
                Country = _services.WorkContext.WorkingLanguage.UniqueSeoCode.ToUpper(),
                Currency = _services.WorkContext.WorkingCurrency.CurrencyCode.ToLower(),
                Total = new StripePaymentItem
                {
                    Amount = subTotalConverted.Amount.ToSmallestCurrencyUnit(),
                    Label = T("Order.SubTotal").Value,
                    Pending = false
                },
                DisplayItems = displayItems
            };

            return stripePaymentRequest;
        }

        public async Task<string> GetWebHookIdAsync(string secrectApiKey, string storeUrl)
        {
            StripeConfiguration.ApiKey = secrectApiKey;

            var service = new WebhookEndpointService();
            var webhooks = await service.ListAsync(new WebhookEndpointListOptions
            {
                Limit = 10
            });

            // Check if webhook already exists
            if (webhooks.Data.Count < 1 || !webhooks.Data.Any(x => x.Url.ContainsNoCase(storeUrl)))
            {
                // Create webhook
                var createOptions = new WebhookEndpointCreateOptions
                {
                    ApiVersion = Module.ApiVersion,
                    Url = storeUrl + "stripe/webhookhandler",
                    EnabledEvents = new List<string>
                    {
                        "payment_intent.succeeded",
                        "payment_intent.canceled",
                        "charge.refunded"
                    }
                };

                var webhook = await service.CreateAsync(createOptions);

                return webhook.Id;
            }
            else
            {
                return webhooks.Data.Where(x => x.Url.ContainsNoCase(storeUrl)).FirstOrDefault()?.Id;
            }
        }

        public Task<bool> IsStripeElementsActive()
            => _paymentService.IsPaymentProviderActiveAsync(StripeElementsProvider.SystemName, null, _services.StoreContext.CurrentStore.Id);

        public Task<bool> IsStripeElementsEnabled()
            => _paymentService.IsPaymentProviderEnabledAsync(StripeElementsProvider.SystemName, _services.StoreContext.CurrentStore.Id);
    }
}
