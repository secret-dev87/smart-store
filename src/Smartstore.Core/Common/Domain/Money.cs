﻿using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Html;
using Newtonsoft.Json;
using Sys = System;

namespace Smartstore.Core.Common
{
    public struct Money : IHtmlContent, IConvertible, IFormattable, IComparable, IComparable<Money>, IEquatable<Money>
    {
        public readonly static Money Zero;
        
        // Key: string = Currency.DisplayLocale, bool = useIsoCodeAsSymbol
        private readonly static ConcurrentDictionary<(string, bool), NumberFormatInfo> _numberFormatClones = new();

        public Money(Currency currency)
            : this(0m, currency)
        {
        }

        public Money(float amount, Currency currency)
            : this((decimal)amount, currency, false)
        {
        }

        public Money(double amount, Currency currency)
            : this((decimal)amount, currency, false)
        {
        }

        public Money(decimal amount, Currency currency)
            : this(amount, currency, false)
        {
        }

        public Money(decimal amount, Currency currency, bool hideCurrency)
        {
            Guard.NotNull(currency, nameof(currency));

            Amount = amount;
            Currency = currency;
            HideCurrency = hideCurrency;
            ShowTax = false;
            TaxSuffixFormatString = null;
        }

        [JsonIgnore]
        public bool HideCurrency
        {
            get;
            internal set;
        }

        [JsonIgnore]
        public Currency Currency
        {
            get;
            internal set;
        }

        /// <summary>
        /// Gets the number of decimal digits for the associated currency.
        /// </summary>
        public int DecimalDigits
        {
            get => Currency?.RoundNumDecimals ?? 2;
        }

        /// <summary>
        /// The internal unrounded raw amount
        /// </summary>
        public decimal Amount
        {
            get;
        }

        // TODO: (mg) (core) UI. Always show Currency.RoundNumDecimals on currency edit page. It is no longer used only for order item rounding.
        /// <summary>
        /// Rounds the amount to the number of significant decimal digits
        /// of the associated currency using MidpointRounding.AwayFromZero.
        /// </summary>
        public decimal RoundedAmount
        {
            get => decimal.Round(Amount, DecimalDigits);
        }

        /// <summary>
        /// Truncates the amount to the number of significant decimal digits
        /// of the associated currency.
        /// </summary>
        public decimal TruncatedAmount
        {
            get => (decimal)((long)Math.Truncate(Amount * DecimalDigits)) / DecimalDigits;
        }

        /// <summary>
        /// A value indicating whether to include tax information when formatting.
        /// </summary>
        public bool ShowTax
        {
            get;
            init;
        }

        /// <summary>
        /// The format string including tax suffix.
        /// </summary>
        public string TaxSuffixFormatString
        {
            get;
            init;
        }

        /// <summary>
        /// The formatted amount.
        /// </summary>
        public string Formatted
        {
            get => ToString(true, false, true);
        }

        private static void GuardCurrenciesAreEqual(Money a, Money b)
        {
            if (a.Currency != b.Currency)
                throw new InvalidOperationException("Cannot operate on money values with different currencies.");
        }

        #region Assign

        public Money Change(float amount, Currency currency = null)
            => Change((decimal)amount);

        public Money Change(double amount, Currency currency = null)
            => Change((decimal)amount);

        public Money Change(decimal amount, Currency currency = null)
        {
            return new Money(amount, currency ?? Currency, HideCurrency)
            {
                ShowTax = ShowTax,
                TaxSuffixFormatString = TaxSuffixFormatString
            };
        }

        #endregion

        #region Compare

        public override int GetHashCode()
        {
            if (Amount == 0)
                return 0;

            return Amount.GetHashCode() ^ (Currency?.GetHashCode() ?? 0);
        }

        public int CompareTo(Money other)
        {
            return ((IComparable)this).CompareTo(other);
        }

        int IComparable.CompareTo(object obj)
        {
            if (obj == null || !(obj is Money))
                return 1;

            Money other = (Money)obj;

            if (this.Amount == other.Amount)
                return 0;
            if (this.Amount < other.Amount)
                return -1;

            return 1;
        }

        public override bool Equals(object obj)
        {
            if (obj is Money money)
            {
                return ((IEquatable<Money>)this).Equals(money);
            }

            return false;
        }

        bool IEquatable<Money>.Equals(Money other)
        {
            if (other.Amount == 0 && this.Amount == 0)
                return true;

            return other.Amount == this.Amount && other.Currency == this.Currency;
        }

        public static bool operator ==(Money a, Money b) => a.Equals(b);
        public static bool operator !=(Money a, Money b) => !a.Equals(b);

        public static bool operator >(Money a, Money b)
        {
            GuardCurrenciesAreEqual(a, b);
            return a.Amount > b.Amount;
        }

        public static bool operator <(Money a, Money b)
        {
            GuardCurrenciesAreEqual(a, b);
            return a.Amount < b.Amount;
        }

        public static bool operator <=(Money a, Money b)
        {
            GuardCurrenciesAreEqual(a, b);
            return a.Amount <= b.Amount;

        }

        public static bool operator >=(Money a, Money b)
        {
            GuardCurrenciesAreEqual(a, b);
            return a.Amount >= b.Amount;

        }

        public static bool operator ==(Money a, int b) => a.Amount == b;
        public static bool operator !=(Money a, int b) => a.Amount != b;
        public static bool operator >(Money a, int b) => a.Amount > b;
        public static bool operator <(Money a, int b) => a.Amount < b;
        public static bool operator <=(Money a, int b) => a.Amount <= b;
        public static bool operator >=(Money a, int b) => a.Amount >= b;

        public static bool operator ==(Money a, float b) => a.Amount == (decimal)b;
        public static bool operator !=(Money a, float b) => a.Amount != (decimal)b;
        public static bool operator >(Money a, float b) => a.Amount > (decimal)b;
        public static bool operator <(Money a, float b) => a.Amount < (decimal)b;
        public static bool operator <=(Money a, float b) => a.Amount <= (decimal)b;
        public static bool operator >=(Money a, float b) => a.Amount >= (decimal)b;

        public static bool operator ==(Money a, double b) => a.Amount == (decimal)b;
        public static bool operator !=(Money a, double b) => a.Amount != (decimal)b;
        public static bool operator >(Money a, double b) => a.Amount > (decimal)b;
        public static bool operator <(Money a, double b) => a.Amount < (decimal)b;
        public static bool operator <=(Money a, double b) => a.Amount <= (decimal)b;
        public static bool operator >=(Money a, double b) => a.Amount >= (decimal)b;

        public static bool operator ==(Money a, decimal b) => a.Amount == b;
        public static bool operator !=(Money a, decimal b) => a.Amount != b;
        public static bool operator >(Money a, decimal b) => a.Amount > b;
        public static bool operator <(Money a, decimal b) => a.Amount < b;
        public static bool operator <=(Money a, decimal b) => a.Amount <= b;
        public static bool operator >=(Money a, decimal b) => a.Amount >= b;

        #endregion

        #region Format

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void IHtmlContent.WriteTo(TextWriter writer, HtmlEncoder encoder)
            => writer.Write(ToString());

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        string IFormattable.ToString(string format, IFormatProvider formatProvider)
            => ToString(!HideCurrency, false, ShowTax);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        string IConvertible.ToString(IFormatProvider provider)
            => ToString(!HideCurrency, false, ShowTax);

        /// <summary>
        /// Creates the string representation of the rounded amount.
        /// </summary>
        /// <returns>The formatted rounded amount.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override string ToString()
            => ToString(!HideCurrency, false, ShowTax);

        /// <summary>
        /// Creates the string representation of the rounded amount.
        /// </summary>
        /// <param name="showCurrency">Whether to render currency symbol. If <c>null</c>, falls back to negated <see cref="HideCurrency"/>.</param>
        /// <param name="useISOCodeAsSymbol">Whether to render currency symbol as ISO code. Only relevant if currency symbol should be rendered.</param>
        /// <param name="showTax">Whether to render tax info suffix (e.g. "incl. tax"). Only relevant if <see cref="TaxSuffixFormatString"/> is not empty.</param>
        /// <returns>The formatted rounded amount.</returns>
        public string ToString(bool? showCurrency = null, bool useISOCodeAsSymbol = false, bool? showTax = null)
        {
            if (Currency == null)
            {
                return RoundedAmount.FormatInvariant();
            }

            showTax ??= ShowTax;
            
            var nf = Currency.NumberFormat;
            var template = showTax == true ? TaxSuffixFormatString : "{0}";

            if (!string.IsNullOrEmpty(Currency.CustomFormatting))
            {
                return string.Format(template, RoundedAmount.ToString(Currency.CustomFormatting, nf));
            }
            else
            {
                showCurrency ??= !HideCurrency;
                if (showCurrency == false || useISOCodeAsSymbol)
                {
                    var currencyCode = Currency.CurrencyCode;
                    nf = Currency.NumberFormat;
                    nf = _numberFormatClones.GetOrAdd((Currency.DisplayLocale, useISOCodeAsSymbol), key => 
                    {
                        var clone = (NumberFormatInfo)nf.Clone();
                        nf.CurrencySymbol = showCurrency == false ? string.Empty : currencyCode;
                        return clone;
                    });
                }

                return string.Format(template, RoundedAmount.ToString("C", nf));
            }
        }

        #endregion

        #region Convert

        // For truthy checks in templating
        public static explicit operator bool(Money money) => money.Amount != 0;
        public static explicit operator string(Money money) => money.ToString(true, false);
        public static explicit operator byte(Money money) => Sys.Convert.ToByte(money.RoundedAmount);
        public static explicit operator decimal(Money money) => money.RoundedAmount;
        public static explicit operator double(Money money) => Sys.Convert.ToDouble(money.RoundedAmount);
        public static explicit operator float(Money money) => Sys.Convert.ToSingle(money.RoundedAmount);
        public static explicit operator int(Money money) => Sys.Convert.ToInt32(money.RoundedAmount);
        public static explicit operator long(Money money) => Sys.Convert.ToInt64(money.RoundedAmount);
        public static explicit operator sbyte(Money money) => Sys.Convert.ToSByte(money.RoundedAmount);
        public static explicit operator short(Money money) => Sys.Convert.ToInt16(money.RoundedAmount);
        public static explicit operator ushort(Money money) => Sys.Convert.ToUInt16(money.RoundedAmount);
        public static explicit operator uint(Money money) => Sys.Convert.ToUInt32(money.RoundedAmount);
        public static explicit operator ulong(Money money) => Sys.Convert.ToUInt64(money.RoundedAmount);

        TypeCode IConvertible.GetTypeCode() => TypeCode.Decimal;
        object IConvertible.ToType(Type conversionType, IFormatProvider provider) => Sys.Convert.ChangeType(this.RoundedAmount, conversionType, provider);
        bool IConvertible.ToBoolean(IFormatProvider provider) => Amount != 0;
        char IConvertible.ToChar(IFormatProvider provider) => throw Error.InvalidCast(typeof(Money), typeof(char));
        DateTime IConvertible.ToDateTime(IFormatProvider provider) => throw Error.InvalidCast(typeof(Money), typeof(DateTime));
        byte IConvertible.ToByte(IFormatProvider provider) => (byte)RoundedAmount;
        decimal IConvertible.ToDecimal(IFormatProvider provider) => RoundedAmount;
        double IConvertible.ToDouble(IFormatProvider provider) => (double)RoundedAmount;
        short IConvertible.ToInt16(IFormatProvider provider) => (short)RoundedAmount;
        int IConvertible.ToInt32(IFormatProvider provider) => (int)RoundedAmount;
        long IConvertible.ToInt64(IFormatProvider provider) => (long)RoundedAmount;
        sbyte IConvertible.ToSByte(IFormatProvider provider) => (sbyte)RoundedAmount;
        float IConvertible.ToSingle(IFormatProvider provider) => (float)RoundedAmount;
        ushort IConvertible.ToUInt16(IFormatProvider provider) => (ushort)RoundedAmount;
        uint IConvertible.ToUInt32(IFormatProvider provider) => (uint)RoundedAmount;
        ulong IConvertible.ToUInt64(IFormatProvider provider) => (ulong)RoundedAmount;

        #endregion

        #region Add

        public static Money operator ++(Money a)
        {
            var amount = a.Amount;
            return a.Change(amount++);
        }

        public static Money operator +(Money a, Money b)
        {
            GuardCurrenciesAreEqual(a, b);
            return a.Change(a.Amount + b.Amount);
        }

        public static Money operator +(Money a, int b) => a + (decimal)b;
        public static Money operator +(Money a, float b) => a + (decimal)b;
        public static Money operator +(Money a, double b) => a + (decimal)b;
        public static Money operator +(Money a, decimal b) => a.Change(a.Amount + b);

        #endregion

        #region Substract

        public static Money operator --(Money a)
        {
            var amount = a.Amount;
            return a.Change(amount--);
        }

        public static Money operator -(Money a, Money b)
        {
            GuardCurrenciesAreEqual(a, b);
            return a.Change(a.Amount - b.Amount);
        }

        public static Money operator -(Money a, int b) => a + (decimal)b;
        public static Money operator -(Money a, float b) => a + (decimal)b;
        public static Money operator -(Money a, double b) => a + (decimal)b;
        public static Money operator -(Money a, decimal b) => a.Change(a.Amount - b);

        #endregion

        #region Multiply

        public static Money operator *(Money a, Money b)
        {
            GuardCurrenciesAreEqual(a, b);
            return a.Change(a.Amount * b.Amount);
        }

        public static Money operator *(Money a, int b) => a * (decimal)b;
        public static Money operator *(Money a, float b) => a * (decimal)b;
        public static Money operator *(Money a, double b) => a * (decimal)b;
        public static Money operator *(Money a, decimal b) => a.Change(a.Amount * b);

        #endregion

        #region Divide

        public static Money operator /(Money a, Money b)
        {
            GuardCurrenciesAreEqual(a, b);
            return a.Change(a.Amount / b.Amount);
        }

        public static Money operator /(Money a, int b) => a / (decimal)b;
        public static Money operator /(Money a, float b) => a / (decimal)b;
        public static Money operator /(Money a, double b) => a / (decimal)b;
        public static Money operator /(Money a, decimal b) => a.Change(a.Amount / b);

        #endregion

        #region Exchange & Math

        /// <summary>
        /// Creates a new <see cref="Money"/> struct with <see cref="RoundedAmount"/> as <see cref="Amount"/>, 
        /// but only if enabled for the currency or if <paramref name="force"/> is <c>true</c>.
        /// </summary>
        /// <param name="force">Round also if disabled for the currency</param>
        /// <returns>A new instance with the rounded amount</returns>
        public Money Round(bool force = false)
        {
            if (Currency != null && (force || Currency.RoundOrderItemsEnabled))
            {
                return Change(RoundedAmount);
            }

            return this;
        }

        public Money Exchange(Currency toCurrency)
        {
            if (Currency == toCurrency)
                return this;

            return new Money((Amount * Currency.Rate) / toCurrency.Rate, toCurrency, HideCurrency)
            {
                ShowTax = ShowTax,
                TaxSuffixFormatString = TaxSuffixFormatString
            };
        }

        /// <summary>
        /// Evenly distributes the amount over n parts, resolving remainders that occur due to rounding 
        /// errors, thereby garuanteeing the postcondition: result->sum(r|r.amount) = this.amount and
        /// x elements in result are greater than at least one of the other elements, where x = amount mod n.
        /// </summary>
        /// <param name="n">Number of parts over which the amount is to be distibuted.</param>
        /// <returns>Array with distributed Money amounts.</returns>
        public Money[] Allocate(int n)
        {
            var cents = Math.Pow(10, DecimalDigits);
            var lowResult = ((long)Math.Truncate((double)Amount / n * cents)) / cents;
            var highResult = lowResult + 1.0d / cents;
            var results = new Money[n];
            var remainder = (int)(((double)Amount * cents) % n);

            for (var i = 0; i < remainder; i++)
                results[i] = Change(highResult);

            for (var i = remainder; i < n; i++)
                results[i] = Change(lowResult);

            return results;
        }

        /// <summary>
        /// Gets the ratio of one money to another.
        /// </summary>
        /// <param name="numerator">The numerator of the operation.</param>
        /// <param name="denominator">The denominator of the operation.</param>
        /// <returns>A decimal from 0.0 to 1.0 of the ratio between the two money values.</returns>
        public static decimal GetRatio(Money numerator, Money denominator)
        {
            if (numerator == 0)
                return 0;

            if (denominator == 0)
                throw new DivideByZeroException("Attempted to divide by zero!");

            GuardCurrenciesAreEqual(numerator, denominator);

            return numerator.Amount / denominator.Amount;
        }

        /// <summary>
        /// Gets the smallest money, given the two values.
        /// </summary>
        /// <param name="m1">The first money to compare.</param>
        /// <param name="m2">The second money to compare.</param>
        /// <returns>The smallest money value of the arguments.</returns>
        public static Money Min(Money a, Money b)
        {
            GuardCurrenciesAreEqual(a, b);

            if (a == b)
                return a;
            else if (a > b)
                return b;
            else
                return a;
        }

        /// <summary>
        /// Gets the largest money, given the two values.
        /// </summary>
        /// <param name="m1">The first money to compare.</param>
        /// <param name="m2">The second money to compare.</param>
        /// <returns>The largest money value of the arguments.</returns>
        public static Money Max(Money a, Money b)
        {
            GuardCurrenciesAreEqual(a, b);

            if (a == b)
                return a;
            else if (a > b)
                return a;
            else
                return b;
        }

        /// <summary>
        /// Gets the absolute value of the <see cref="Money"/>.
        /// </summary>
        /// <param name="value">The value of money to convert.</param>
        /// <returns>The money value as an absolute value.</returns>
        public static Money Abs(Money value)
        {
            return value.Change(Math.Abs(value.Amount));
        }

        #endregion
    }
}
