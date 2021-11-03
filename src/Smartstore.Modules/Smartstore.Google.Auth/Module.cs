﻿using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Smartstore.Core.Widgets;
using Smartstore.Engine.Modularity;
using Smartstore.Http;
using Smartstore.Google.Auth.Components;
using Smartstore.Core.Identity;

namespace Smartstore.Google.Auth
{
    internal class Module : ModuleBase, IConfigurable, IExternalAuthenticationMethod
    {
        public RouteInfo GetConfigurationRoute()
            => new("Configure", "GoogleAuth", new { area = "Admin" });

        public WidgetInvoker GetDisplayWidget(int storeId)
            => new ComponentWidgetInvoker(typeof(GoogleAuthViewComponent), null);

        public override async Task InstallAsync(ModuleInstallationContext context)
        {
            await ImportLanguageResourcesAsync();
            await base.InstallAsync(context);
        }

        public override async Task UninstallAsync()
        {
            await DeleteLanguageResourcesAsync();
            await base.UninstallAsync();
        }
    }
}
