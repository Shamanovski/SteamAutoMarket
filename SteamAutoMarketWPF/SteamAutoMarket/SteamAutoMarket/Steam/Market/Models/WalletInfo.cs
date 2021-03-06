﻿namespace SteamAutoMarket.Steam.Market.Models
{
    using System;

    [Serializable]
    public class WalletInfo
    {
        public int Currency { get; set; }

        public double MaxBalance { get; set; }

        public double WalletBalance { get; set; }

        public string WalletCountry { get; set; }

        public double WalletFee { get; set; }

        public double WalletFeeMinimum { get; set; }

        public int WalletFeePercent { get; set; }

        public int WalletPublisherFeePercent { get; set; }

        public double WalletTradeMaxBalance { get; set; }
    }
}