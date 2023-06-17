﻿using Smartstore.Core;
using Smartstore.Core.Checkout.Orders;
using Smartstore.Core.Checkout.Payment;
using Smartstore.Core.Configuration;

namespace Smartstore.PayPal.Services
{
    /// <summary>
    /// Filters out Pay Upon Invoice if the cart total is above upper limit.
    /// </summary>
    public partial class PayUponInvoicePaymentFilter : IPaymentMethodFilter
    {
        private readonly ICommonServices _services;
        private readonly Lazy<ISettingFactory> _settingFactory;
        private readonly IOrderCalculationService _orderCalculationService;
        
        public PayUponInvoicePaymentFilter(ICommonServices services, Lazy<ISettingFactory> settingFactory, IOrderCalculationService orderCalculationService)
        {
            _services = services;
            _settingFactory = settingFactory;
            _orderCalculationService = orderCalculationService;
        }

        public async Task<bool> IsExcludedAsync(PaymentFilterRequest request)
        {
            if (request.PaymentProvider.Metadata.SystemName.EqualsNoCase("Payments.PayPalPayUponInvoice"))
            {
                var settings = await _settingFactory.Value.LoadSettingsAsync<PayPalSettings>(request.StoreId);

                // If client id or secret haven't been configured yet, don't render payment method.
                if (!settings.ClientId.HasValue() || !settings.Secret.HasValue())
                {
                    return true;
                }

                if (request.Cart != null)
                {
                    // Do not render Pay Upon Invoice if order total is above limit or below 5 €.
                    // Info: it's save to use € here directly, because PayPal offers Pay Upon Invoice only in Germany.
                    var cartTotal = await _orderCalculationService.GetShoppingCartTotalAsync(request.Cart);
                    if (cartTotal.Total.HasValue 
                        && cartTotal.Total.Value.Amount >= settings.PayUponInvoiceLimit
                        && cartTotal.Total.Value.Amount <= 5
                        && _services.WorkContext.WorkingCurrency.CurrencyCode == "EUR")
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}