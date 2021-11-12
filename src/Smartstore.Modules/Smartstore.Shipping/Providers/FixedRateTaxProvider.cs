﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Smartstore.Core.Checkout.Shipping;
using Smartstore.Core.Common;
using Smartstore.Core.Configuration;
using Smartstore.Core.Localization;
using Smartstore.Engine.Modularity;
using Smartstore.Http;

namespace Smartstore.Shipping
{
    [SystemName("Shipping.FixedRate")]
    [FriendlyName("Fixed Rate Shipping")]
    [Order(0)]
    internal class FixedRateProvider : IShippingRateComputationMethod, IConfigurable
    {
        private readonly IShippingService _shippingService;
        private readonly ISettingService _settingService;

        public FixedRateProvider(IShippingService shippingService, ISettingService settingService)
        {
            _shippingService = shippingService;
            _settingService = settingService;

            T = NullLocalizer.Instance;
        }

        public Localizer T { get; set; }

        private Money GetRate(int shippingMethodId)
        {
            string key = $"ShippingRateComputationMethod.FixedRate.Rate.ShippingMethodId{shippingMethodId}";

            // TODO: (mh) (core) Which currency should be passed here?
            Money rate = new(_settingService.GetSettingByKey<decimal>(key), null);
            return rate;
        }

        public RouteInfo GetConfigurationRoute()
            => new("Configure", "FixedRate", new { area = "Admin" });

        public async Task<Money?> GetFixedRateAsync(ShippingOptionRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            var shippingMethods = await _shippingService.GetAllShippingMethodsAsync(request.StoreId);

            var rates = new List<Money>();
            foreach (var shippingMethod in shippingMethods)
            {
                Money rate = GetRate(shippingMethod.Id);
                if (!rates.Contains(rate))
                {
                    rates.Add(rate);
                }
            }

            // Return default rate if all of them are equal.
            if (rates.Count == 1)
            {
                return rates[0];
            }

            return null;
        }

        public async Task<ShippingOptionResponse> GetShippingOptionsAsync(ShippingOptionRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            var response = new ShippingOptionResponse();

            if (request.Items == null || request.Items.Count == 0)
            {
                response.Errors.Add(T("Admin.System.Warnings.NoShipmentItems"));
                return response;
            }

            var shippingMethods = await _shippingService.GetAllShippingMethodsAsync(request.StoreId);
            foreach (var shippingMethod in shippingMethods)
            {
                var shippingOption = new ShippingOption
                {
                    ShippingMethodId = shippingMethod.Id,
                    Name = shippingMethod.GetLocalized(x => x.Name),
                    Description = shippingMethod.GetLocalized(x => x.Description),
                    Rate = GetRate(shippingMethod.Id)
                };
                response.ShippingOptions.Add(shippingOption);
            }

            return response;
        }

        /// <summary>
        /// Gets a shipping rate computation method type
        /// </summary>
        public ShippingRateComputationMethodType ShippingRateComputationMethodType => ShippingRateComputationMethodType.Offline;

        /// <summary>
        /// Gets a shipment tracker
        /// </summary>
        public IShipmentTracker ShipmentTracker =>
                //uncomment a line below to return a general shipment tracker (finds an appropriate tracker by tracking number)
                //return new GeneralShipmentTracker(EngineContext.Current.Resolve<ITypeFinder>());
                null;

        public bool IsActive => true;
    }
}
