﻿using Smartstore.Web.Modelling;

namespace Smartstore.Web.Models.Checkout
{
    public partial class CheckoutCompletedModel : ModelBase
    {
        public int OrderId { get; set; }
        public string OrderNumber { get; set; }
    }
}
