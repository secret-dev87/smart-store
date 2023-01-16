﻿using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Smartstore.Core;
using Smartstore.Core.Checkout.Orders;
using Smartstore.Core.Checkout.Payment;
using Smartstore.Core.Data;
using Smartstore.Core.Logging;
using Smartstore.Core.Widgets;
using Smartstore.PayPal.Client;
using Smartstore.PayPal.Components;
using Smartstore.PayPal.Services;
using Smartstore.Web.Models.Checkout;

namespace Smartstore.PayPal.Filters
{
    public class CheckoutFilter : IAsyncResultFilter
    {
        private readonly SmartDbContext _db;
        private readonly ICommonServices _services;
        private readonly PayPalSettings _settings;
        private readonly ICheckoutStateAccessor _checkoutStateAccessor;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly Lazy<IWidgetProvider> _widgetProvider;
        private readonly PayPalHelper _payPalHelper;
        private readonly PayPalHttpClient _client;

        public CheckoutFilter(
            SmartDbContext db,
            ICommonServices services,
            PayPalSettings settings,
            ICheckoutStateAccessor checkoutStateAccessor,
            IHttpContextAccessor httpContextAccessor,
            Lazy<IWidgetProvider> widgetProvider,
            PayPalHelper payPalHelper,
            PayPalHttpClient client)
        {
            _db = db;
            _services = services;
            _settings = settings;
            _checkoutStateAccessor = checkoutStateAccessor;
            _httpContextAccessor = httpContextAccessor;
            _widgetProvider = widgetProvider;
            _payPalHelper = payPalHelper;
            _client = client;
        }

        public ILogger Logger { get; set; } = NullLogger.Instance;

        public async Task OnResultExecutionAsync(ResultExecutingContext filterContext, ResultExecutionDelegate next)
        {
            if (!await _payPalHelper.IsPayPalStandardActiveAsync())
            {
                await next();
                return;
            }

            // If client id or secret haven't been configured yet, don't do anything.
            if (!_settings.ClientId.HasValue() || !_settings.Secret.HasValue())
            {
                await next();
                return;
            }

            var checkoutState = _checkoutStateAccessor.CheckoutState;

            if (!checkoutState.CustomProperties.ContainsKey("PayPalButtonUsed"))
            {
                if (filterContext.Result is not ViewResult viewResult || viewResult.Model is not CheckoutPaymentMethodModel model)
                {
                    await next();
                    return;
                }

                var isSelected = false;
                var firstPaymentMethod = model.PaymentMethods.First(); 
                if (firstPaymentMethod != null)
                {
                    isSelected = firstPaymentMethod.PaymentMethodSystemName == "Payments.PayPalStandard" && firstPaymentMethod.Selected;
                }

                _widgetProvider.Value.RegisterViewComponent<PayPalViewComponent>("checkout_payment_method_buttons", new { isPaymentInfoInvoker = false, isSelected });

                await next();
                return;
            }

            var skipPaymentPage = (bool)checkoutState.CustomProperties.Get("PayPalButtonUsed");

            // Should only run on a full view rendering result or HTML ContentResult.
            if ((filterContext.Result is StatusCodeResult || filterContext.Result.IsHtmlViewResult()) && skipPaymentPage)
            {
                _services.WorkContext.CurrentCustomer.GenericAttributes.SelectedPaymentMethod = "Payments.PayPalStandard";
                await _db.SaveChangesAsync();

                var session = _httpContextAccessor.HttpContext.Session;

                if (!session.TryGetObject<ProcessPaymentRequest>("OrderPaymentInfo", out var processPaymentRequest))
                {
                    processPaymentRequest = new ProcessPaymentRequest
                    {
                        PayPalOrderId = (string)checkoutState.CustomProperties.Get("PayPalOrderId"),
                        StoreId = _services.StoreContext.CurrentStore.Id,
                        CustomerId = _services.WorkContext.CurrentCustomer.Id,
                        PaymentMethodSystemName = "Payments.PayPalStandard"
                    };
                    session.TrySetObject("OrderPaymentInfo", processPaymentRequest);
                };

                try
                {
                    var result = new ProcessPaymentResult
                    {
                        NewPaymentStatus = PaymentStatus.Pending,
                    };

                    await _client.UpdateOrderAsync(processPaymentRequest, result);

                    // Set flag which indicates this order was already updated.
                    checkoutState.CustomProperties["PayPalOrderUpdated"] = true;
                }
                catch (Exception ex)
                {
                    Logger.LogInformation(ex, "PayPal order couldn't be updated. Redirected to paqyment selction page.");

                    // Remove PayPalButtonUsed flag so payment selection won't be skipped anymore.
                    checkoutState.CustomProperties.Remove("PayPalButtonUsed");

                    // Notify customer about failed order update.
                    _services.Notifier.Error(_services.Localization.GetResource("Plugins.Smartstore.PayPal.OrderUpdateFailed").ToString());

                    // Redirect to payment selection.
                    filterContext.Result = new RedirectToActionResult("PaymentMethod", "Checkout", new { area = "" });
                    await next();
                    return;
                }

                // Delete property for backward navigation.
                checkoutState.CustomProperties.Remove("PayPalButtonUsed");

                filterContext.Result = new RedirectToActionResult("Confirm", "Checkout", new { area = "" });
            }

            await next();
        }
    }
}