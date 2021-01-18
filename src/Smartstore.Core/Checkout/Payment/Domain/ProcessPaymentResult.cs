﻿namespace Smartstore.Core.Checkout.Payment
{
    /// <summary>
    /// Represents a process payment result.
    /// </summary>
    public partial class ProcessPaymentResult : PaymentResult
    {
        /// <summary>
        /// Gets or sets an AVS result.
        /// </summary>
        public string AvsResult { get; set; }

        /// <summary>
        /// Gets or sets the authorization transaction identifier.
        /// </summary>
        public string AuthorizationTransactionId { get; set; }

        /// <summary>
        /// Gets or sets the authorization transaction code.
        /// </summary>
        public string AuthorizationTransactionCode { get; set; }

        /// <summary>
        /// Gets or sets the authorization transaction result.
        /// </summary>
        public string AuthorizationTransactionResult { get; set; }

        /// <summary>
        /// Gets or sets the capture transaction identifier.
        /// </summary>
        public string CaptureTransactionId { get; set; }

        /// <summary>
        /// Gets or sets the capture transaction result.
        /// </summary>
        public string CaptureTransactionResult { get; set; }

        /// <summary>
        /// Gets or sets the subscription transaction identifier.
        /// </summary>
        public string SubscriptionTransactionId { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether storing of credit card number, CVV2 is allowed.
        /// </summary>
        public bool AllowStoringCreditCardNumber { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether storing of credit card number, CVV2 is allowed.
        /// </summary>
        public bool AllowStoringDirectDebit { get; set; }
    }
}
