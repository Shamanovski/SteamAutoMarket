﻿namespace SteamAutoMarket.UI.Models
{
    using System;
    using System.Collections.ObjectModel;
    using System.ComponentModel;
    using System.Linq;
    using System.Runtime.CompilerServices;
    using System.Threading.Tasks;
    using SteamAutoMarket.Properties;
    using SteamAutoMarket.Steam;
    using SteamAutoMarket.Steam.TradeOffer.Models.Full;
    using SteamAutoMarket.UI.Repository.Image;

    [Serializable]
    public class SteamItemsModel : INotifyPropertyChanged
    {
        private double? averagePrice;

        private int count;

        private double? currentPrice;

        private string image;

        public SteamItemsModel(FullRgItem[] itemsList)
        {
            this.ItemsList = new ObservableCollection<FullRgItem>(itemsList);

            this.ItemModel = itemsList.FirstOrDefault();

            this.Count = itemsList.Select(i => int.Parse(i.Asset.Amount)).Sum();

            this.ItemName = this.ItemModel?.Description.MarketName;

            this.Game = this.ItemModel?.Description?.Tags?.FirstOrDefault(tag => tag.Category == "Game")
                ?.LocalizedTagName;

            this.Type = SteamUtils.GetClearItemType(this.ItemModel?.Description?.Type);

            this.Description = new Lazy<string>(() => SteamUtils.GetClearDescription(this.ItemModel));

            this.NumericUpDown = new NumericUpDownModel(this.Count);
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public double? AveragePrice
        {
            get => this.averagePrice;
            set
            {
                this.averagePrice = value;
                this.OnPropertyChanged();
            }
        }

        public int Count
        {
            get => this.count;
            private set
            {
                if (this.count == value) return;

                this.count = value;
                this.OnPropertyChanged();
            }
        }

        public double? CurrentPrice
        {
            get => this.currentPrice;
            set
            {
                this.currentPrice = value;
                this.OnPropertyChanged();
            }
        }

        public Lazy<string> Description { get; }

        public string Game { get; }

        public bool IsImageNotLoaded => this.image == null;

        public string Image
        {
            get
            {
                if (this.image != null) return this.image;

                var imageHashName = this.ItemModel?.Description?.MarketHashName;
                if (ImageCache.IsImageCached(imageHashName))
                {
                    ImageCache.TryGetImage(imageHashName, out this.image);

                    // ReSharper disable once ExplicitCallerInfoArgument
                    this.OnPropertyChanged("IsImageNotLoaded");
                    return this.image;
                }

                Task.Run(
                    () =>
                    {
                        var downloadedImage = ImageProvider.GetItemImage(
                            imageHashName,
                            this.ItemModel?.Description?.IconUrlLarge ?? this.ItemModel?.Description?.IconUrl);

                        this.image = downloadedImage;
                        this.OnPropertyChanged();

                        // ReSharper disable once ExplicitCallerInfoArgument
                        this.OnPropertyChanged("IsImageNotLoaded");
                    });

                return null;
            }

            set
            {
                this.image = value;
                this.OnPropertyChanged();
            }
        }

        public FullRgItem ItemModel { get; }

        public string ItemName { get; }

        public ObservableCollection<FullRgItem> ItemsList { get; }

        public NumericUpDownModel NumericUpDown { get; }

        public string Type { get; }

        public virtual void CleanItemPrices()
        {
            this.CurrentPrice = null;
            this.AveragePrice = null;
        }

        public void RefreshCount()
        {
            this.Count = this.ItemsList.Count;
            this.NumericUpDown.MaxAllowedCount = this.Count;
            if (this.NumericUpDown.AmountToSell > this.Count)
            {
                this.NumericUpDown.AmountToSell = 0;
            }
        }

        [NotifyPropertyChangedInvocator]
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
