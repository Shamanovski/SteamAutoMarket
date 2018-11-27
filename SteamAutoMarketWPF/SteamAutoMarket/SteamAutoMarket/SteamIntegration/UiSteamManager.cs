﻿namespace SteamAutoMarket.SteamIntegration
{
    using System;
    using System.Collections.ObjectModel;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;

    using Core;
    using Core.Waiter;

    using Steam;
    using Steam.Auth;
    using Steam.Market.Enums;
    using Steam.Market.Exceptions;
    using Steam.Market.Models;
    using Steam.TradeOffer.Models;
    using Steam.TradeOffer.Models.Full;

    using SteamAutoMarket.Models;
    using SteamAutoMarket.Pages;
    using SteamAutoMarket.Repository.Context;
    using SteamAutoMarket.Repository.PriceCache;
    using SteamAutoMarket.Repository.Settings;
    using SteamAutoMarket.Utils.Extension;
    using SteamAutoMarket.Utils.Logger;

    using SteamKit2;

    public class UiSteamManager : SteamManager
    {
        public UiSteamManager(SettingsSteamAccount account, bool forceSessionRefresh = false)
            : base(
                account.Login,
                account.Password,
                account.Mafile,
                account.SteamApi,
                account.TradeToken,
                account.Currency,
                SettingsProvider.GetInstance().UserAgent,
                forceSessionRefresh)
        {
            this.SaveAccount(account);

            this.CurrentPriceCache = PriceCacheProvider.GetCurrentPriceCache(
                $"{this.MarketClient.CurrentCurrency}",
                SettingsProvider.GetInstance().CurrentPriceHoursToBecomeOld);

            this.AveragePriceCache = PriceCacheProvider.GetAveragePriceCache(
                $"{this.MarketClient.CurrentCurrency}",
                SettingsProvider.GetInstance().AveragePriceDays,
                SettingsProvider.GetInstance().AveragePriceHoursToBecomeOld);
        }

        public PriceCache AveragePriceCache { get; set; }

        public PriceCache CurrentPriceCache { get; set; }

        public void ConfirmMarketTransactionsWorkingProcess()
        {
            var wp = UiGlobalVariables.WorkingProcessDataContext;

            wp.AppendLog("Fetching confirmations");
            try
            {
                var confirmations = this.Guard.FetchConfirmations();
                var marketConfirmations = confirmations
                    .Where(item => item.ConfType == Confirmation.ConfirmationType.MarketSellTransaction).ToArray();
                wp.AppendLog($"{marketConfirmations.Length} confirmations found. Accepting confirmations");
                this.Guard.AcceptMultipleConfirmations(marketConfirmations);
                wp.AppendLog($"{marketConfirmations.Length} confirmations was successfully accepted");
            }
            catch (Exception e)
            {
                wp.AppendLog($"Error on 2FA confirm - {e.Message}");
            }
        }

        public override double? GetAveragePrice(int appid, string hashName, int days)
        {
            var price = base.GetAveragePrice(appid, hashName, days);
            if (price.HasValue) this.AveragePriceCache.Cache(hashName, price.Value);
            return price;
        }

        public double? GetAveragePriceWithCache(int appid, string hashName, int days) =>
            this.AveragePriceCache.Get(hashName)?.Price ?? this.GetAveragePrice(appid, hashName, days);

        public override double? GetCurrentPrice(int appid, string hashName)
        {
            var price = base.GetCurrentPrice(appid, hashName);
            if (price.HasValue) this.CurrentPriceCache.Cache(hashName, price.Value);
            return price;
        }

        public double? GetCurrentPriceWithCache(int appid, string hashName) =>
            this.CurrentPriceCache.Get(hashName)?.Price ?? this.GetCurrentPrice(appid, hashName);

        public void LoadItemsToSaleWorkingProcess(
            SteamAppId appid,
            int contextId,
            ObservableCollection<MarketSellModel> marketSellItems)
        {
            var wp = UiGlobalVariables.WorkingProcessDataContext;

            WorkingProcess.ProcessMethod(
                () =>
                    {
                        try
                        {
                            wp.AppendLog($"{appid.AppId}-{contextId} inventory loading started");

                            var page = this.LoadInventoryPage(this.SteamId, appid.AppId, contextId);
                            wp.AppendLog($"{page.TotalInventoryCount} items found");

                            var totalPagesCount = (int)Math.Ceiling(page.TotalInventoryCount / 5000d);
                            var currentPage = 1;

                            wp.ProgressBarMaximum = totalPagesCount;
                            this.ProcessMarketSellInventoryPage(marketSellItems, page);

                            wp.AppendLog($"Page {currentPage++}/{totalPagesCount} loaded");
                            wp.IncrementProgress();

                            while (page.MoreItems == 1)
                            {
                                if (wp.CancellationToken.IsCancellationRequested)
                                {
                                    wp.AppendLog($"{appid.Name} inventory loading was force stopped");
                                    return;
                                }

                                page = this.LoadInventoryPage(this.SteamId, appid.AppId, contextId, page.LastAssetid);

                                this.ProcessMarketSellInventoryPage(marketSellItems, page);

                                wp.AppendLog($"Page {currentPage++}/{totalPagesCount} loaded");
                                wp.IncrementProgress();
                            }

                            wp.AppendLog(
                                marketSellItems.Any()
                                    ? $"{marketSellItems.ToArray().Sum(i => i.Count)} marketable items was loaded"
                                    : $"Seems like no items found on {appid.Name} inventory");
                        }
                        catch (Exception e)
                        {
                            var message = $"Error on {appid.Name} inventory loading";

                            wp.AppendLog(message);
                            ErrorNotify.CriticalMessageBox(message, e);
                            marketSellItems.ClearDispatch();
                        }
                    },
                $"{appid.Name} inventory loading");
        }

        public void LoadItemsToTradeWorkingProcess(
            SteamAppId appid,
            int contextId,
            ObservableCollection<SteamItemsModel> itemsToTrade,
            bool onlyUnmarketable)
        {
            var wp = UiGlobalVariables.WorkingProcessDataContext;

            WorkingProcess.ProcessMethod(
                () =>
                    {
                        try
                        {
                            wp.AppendLog($"{appid.AppId}-{contextId} inventory loading started");

                            var page = this.LoadInventoryPage(this.SteamId, appid.AppId, contextId);
                            wp.AppendLog($"{page.TotalInventoryCount} items found");

                            var totalPagesCount = (int)Math.Ceiling(page.TotalInventoryCount / 5000d);
                            var currentPage = 1;

                            wp.ProgressBarMaximum = totalPagesCount;
                            this.ProcessTradeSendInventoryPage(itemsToTrade, page, onlyUnmarketable);

                            wp.AppendLog($"Page {currentPage++}/{totalPagesCount} loaded");
                            wp.IncrementProgress();

                            while (page.MoreItems == 1)
                            {
                                if (wp.CancellationToken.IsCancellationRequested)
                                {
                                    wp.AppendLog($"{appid.Name} inventory loading was force stopped");
                                    return;
                                }

                                page = this.LoadInventoryPage(this.SteamId, appid.AppId, contextId, page.LastAssetid);

                                this.ProcessTradeSendInventoryPage(itemsToTrade, page, onlyUnmarketable);

                                wp.AppendLog($"Page {currentPage++}/{totalPagesCount} loaded");
                                wp.IncrementProgress();
                            }

                            wp.AppendLog(
                                itemsToTrade.Any()
                                    ? $"{itemsToTrade.ToArray().Sum(i => i.Count)} tradable items was loaded"
                                    : $"Seems like no items found on {appid.Name} inventory");
                        }
                        catch (Exception e)
                        {
                            var message = $"Error on {appid.Name} inventory loading";

                            wp.AppendLog(message);
                            ErrorNotify.CriticalMessageBox(message, e);
                            itemsToTrade.ClearDispatch();
                        }
                    },
                $"{appid.Name} inventory loading");
        }

        public void LoadMarketListings(ObservableCollection<MarketRelistModel> relistItemsList)
        {
            var wp = UiGlobalVariables.WorkingProcessDataContext;
            WorkingProcess.ProcessMethod(
                () =>
                    {
                        try
                        {
                            wp.AppendLog("Market listings loading started");

                            var page = this.MarketClient.FetchSellOrders();

                            wp.AppendLog($"{page.TotalCount} items found");
                            var totalCount = page.TotalCount;
                            var totalPagesCount = (int)Math.Ceiling(totalCount / 100d);
                            var currentPage = 1;
                            wp.ProgressBarMaximum = totalPagesCount;

                            this.ProcessMarketSellListingsPage(page, relistItemsList);
                            wp.AppendLog($"Page {currentPage++}/{totalPagesCount} loaded");
                            wp.IncrementProgress();

                            for (var startItemIndex = 100; startItemIndex < totalCount; startItemIndex += 100)
                            {
                                if (wp.CancellationToken.IsCancellationRequested)
                                {
                                    wp.AppendLog("Market listings loading was force stopped");
                                    return;
                                }

                                page = this.MarketClient.FetchSellOrders(startItemIndex);
                                this.ProcessMarketSellListingsPage(page, relistItemsList);
                                wp.AppendLog($"Page {currentPage++}/{totalPagesCount} loaded");
                                wp.IncrementProgress();
                            }
                        }
                        catch (Exception e)
                        {
                            const string Message = "Error on market listings loading";
                            wp.AppendLog(Message);
                            ErrorNotify.CriticalMessageBox(Message, e);
                            relistItemsList.ClearDispatch();
                        }
                    },
                "Market listings loading");
        }

        public void RelistListings(
            Task[] priceLoadTasksList,
            MarketRelistModel[] marketRelistModels,
            MarketSellStrategy sellStrategy)
        {
            var wp = UiGlobalVariables.WorkingProcessDataContext;
            WorkingProcess.ProcessMethod(
                () =>
                    {
                        try
                        {
                            var notReadyTasksCount = priceLoadTasksList.Count(task => task.IsCompleted == false);
                            if (notReadyTasksCount > 0)
                            {
                                wp.AppendLog(
                                    $"Waiting for {notReadyTasksCount} not finished price load threads to avoid steam ban on requests");
                                new Waiter { Timeout = TimeSpan.FromMinutes(1) }.UntilSoft(
                                    () => priceLoadTasksList.All(task => task.IsCompleted));
                            }

                            var currentItemIndex = 1;
                            var totalItemsCount = marketRelistModels.Sum(x => x.ItemsList.Count);
                            wp.ProgressBarMaximum = totalItemsCount;

                            var threadsCount = SettingsProvider.GetInstance().RelistThreadsCount;
                            var semaphore = new Semaphore(threadsCount, threadsCount);
                            foreach (var marketSellModel in marketRelistModels)
                            {
                                var packageElementIndex = 1;
                                foreach (var item in marketSellModel.ItemsList)
                                {
                                    if (wp.CancellationToken.IsCancellationRequested)
                                    {
                                        wp.AppendLog("Market relist process was force stopped");
                                        return;
                                    }

                                    try
                                    {
                                        semaphore.WaitOne();

                                        wp.AppendLog(
                                            $"[{currentItemIndex}/{totalItemsCount}] Removing - [{packageElementIndex++}/{marketSellModel.Count}] - '{marketSellModel.ItemName}'");

                                        var itemCancelId = item.SaleId;
                                        var realIndex = currentItemIndex;
                                        Task.Run(
                                            () =>
                                                {
                                                    try
                                                    {
                                                        var result = this.MarketClient.CancelSellOrder(itemCancelId);
                                                        if (result != ECancelSellOrderStatus.Canceled)
                                                        {
                                                            wp.AppendLog(
                                                                $"[{realIndex}/{totalItemsCount}] - Error on canceling sell order {marketSellModel.ItemName}");
                                                        }
                                                    }
                                                    catch (Exception ex)
                                                    {
                                                        wp.AppendLog(
                                                            $"Error on canceling sell order {marketSellModel.ItemName} - {ex.Message}");
                                                        Logger.Log.Error(ex);
                                                    }

                                                    wp.IncrementProgress();
                                                    semaphore.Release();
                                                });
                                    }
                                    catch (Exception ex)
                                    {
                                        wp.AppendLog(
                                            $"Error on canceling sell order {marketSellModel.ItemName} - {ex.Message}");
                                        Logger.Log.Error(ex);
                                    }

                                    currentItemIndex++;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            var message = $"Critical error on market relist - {ex.Message}";
                            wp.AppendLog(message);
                            ErrorNotify.CriticalMessageBox(message);
                        }
                    },
                "Market relist");
        }

        public void SaveAccount(SettingsSteamAccount account)
        {
            if (this.IsSessionUpdated == false) return;
            this.IsSessionUpdated = false;

            account.SteamApi = this.ApiKey;
            account.TradeToken = this.TradeToken;
            account.Currency = this.Currency;
            SettingsProvider.GetInstance().OnPropertyChanged("SteamAccounts");
        }

        public void SellOnMarket(
            Task[] priceLoadTasksList,
            MarketSellProcessModel[] marketSellModels,
            MarketSellStrategy sellStrategy)
        {
            var wp = UiGlobalVariables.WorkingProcessDataContext;
            WorkingProcess.ProcessMethod(
                () =>
                    {
                        try
                        {
                            var notReadyTasksCount = priceLoadTasksList.Count(task => task.IsCompleted == false);
                            if (notReadyTasksCount > 0)
                            {
                                wp.AppendLog(
                                    $"Waiting for {notReadyTasksCount} not finished price load threads to avoid steam ban on requests");
                                new Waiter { Timeout = TimeSpan.FromMinutes(1) }.UntilSoft(
                                    () => priceLoadTasksList.All(task => task.IsCompleted));
                            }

                            var currentItemIndex = 1;
                            var totalItemsCount = marketSellModels.Sum(x => x.Count);
                            wp.ProgressBarMaximum = totalItemsCount;
                            var averagePriceDays = SettingsProvider.GetInstance().AveragePriceDays;

                            foreach (var marketSellModel in marketSellModels)
                            {
                                if (!marketSellModel.SellPrice.HasValue)
                                {
                                    wp.AppendLog(
                                        $"Price for '{marketSellModel.ItemName}' is not loaded. Processing price");
                                    if (wp.CancellationToken.IsCancellationRequested)
                                    {
                                        wp.AppendLog("Market sell process was force stopped");
                                        return;
                                    }

                                    try
                                    {
                                        var price = this.GetAveragePriceWithCache(
                                            marketSellModel.ItemModel.Asset.Appid,
                                            marketSellModel.ItemModel.Description.MarketHashName,
                                            averagePriceDays);

                                        wp.AppendLog(
                                            $"Average price for {averagePriceDays} days for '{marketSellModel.ItemName}' is - {price}");

                                        marketSellModel.AveragePrice = price;

                                        price = this.GetCurrentPriceWithCache(
                                            marketSellModel.ItemModel.Asset.Appid,
                                            marketSellModel.ItemModel.Description.MarketHashName);

                                        wp.AppendLog($"Current price for '{marketSellModel.ItemName}' is - {price}");
                                        marketSellModel.CurrentPrice = price;
                                        marketSellModel.ProcessSellPrice(sellStrategy);
                                    }
                                    catch (Exception ex)
                                    {
                                        wp.AppendLog($"Error on market price parse - {ex.Message}");
                                        wp.AppendLog($"Skipping '{marketSellModel.ItemName}'");

                                        totalItemsCount -= marketSellModel.Count;
                                        wp.ProgressBarMaximum = totalItemsCount;
                                        continue;
                                    }
                                }

                                var packageElementIndex = 1;
                                var errorsCount = 0;
                                foreach (var item in marketSellModel.ItemsList)
                                {
                                    if (wp.CancellationToken.IsCancellationRequested)
                                    {
                                        wp.AppendLog("Market sell process was force stopped");
                                        return;
                                    }

                                    try
                                    {
                                        wp.AppendLog(
                                            $"[{currentItemIndex}/{totalItemsCount}] Selling - [{packageElementIndex++}/{marketSellModel.Count}] - '{marketSellModel.ItemName}' for {marketSellModel.SellPrice}");

                                        if (marketSellModel.SellPrice.HasValue)
                                        {
                                            this.SellOnMarket(item, marketSellModel.SellPrice.Value);
                                        }
                                        else
                                        {
                                            wp.AppendLog(
                                                $"Error on selling '{marketSellModel.ItemName}' - Price is not loaded. Skipping item.");
                                            totalItemsCount -= marketSellModel.Count;
                                            wp.ProgressBarMaximum = totalItemsCount;
                                            break;
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        wp.AppendLog($"Error on selling '{marketSellModel.ItemName}' - {ex.Message}");
                                        Logger.Log.Error(ex);
                                        if (++errorsCount == SettingsProvider.GetInstance().ErrorsOnSellToSkip)
                                        {
                                            wp.AppendLog(
                                                $"{SettingsProvider.GetInstance().ErrorsOnSellToSkip} fails limit on sell {marketSellModel.ItemName} reached. Skipping item.");
                                            totalItemsCount -= marketSellModel.Count - packageElementIndex;
                                            wp.ProgressBarMaximum = totalItemsCount;
                                            break;
                                        }
                                    }

                                    if (currentItemIndex % SettingsProvider.GetInstance().ItemsToTwoFactorConfirm == 0)
                                    {
                                        Task.Run(() => this.ConfirmMarketTransactionsWorkingProcess());
                                    }

                                    currentItemIndex++;
                                    wp.IncrementProgress();
                                }
                            }

                            this.ConfirmMarketTransactionsWorkingProcess();
                        }
                        catch (Exception ex)
                        {
                            var message = $"Critical error on market sell - {ex.Message}";
                            wp.AppendLog(message);
                            ErrorNotify.CriticalMessageBox(message);
                        }
                    },
                "Market sell");
        }

        public void SendTrade(string targetSteamId, string tradeToken, FullRgItem[] itemsToTrade, bool acceptTwoFactor)
        {
            var wp = UiGlobalVariables.WorkingProcessDataContext;

            WorkingProcess.ProcessMethod(
                () =>
                    {
                        try
                        {
                            var targetSteamIdObj = targetSteamId.StartsWith("76561198")
                                                       ? new SteamID(ulong.Parse(targetSteamId))
                                                       : new SteamID(
                                                           uint.Parse(targetSteamId),
                                                           EUniverse.Public,
                                                           EAccountType.Individual);

                            wp.AppendLog($"Sending trade offer to {targetSteamIdObj.ConvertToUInt64()} - {tradeToken}");

                            var tradeId = this.SendTradeOffer(itemsToTrade, targetSteamIdObj, tradeToken);
                            if (string.IsNullOrEmpty(tradeId))
                            {
                                throw new SteamException("Steam returned empty trade id");
                            }

                            wp.AppendLog($"Sent trade offer id is - {tradeId}");

                            if (acceptTwoFactor)
                            {
                                var numberTradeId = ulong.Parse(tradeId);
                                try
                                {
                                    this.ConfirmTradeTransactions(numberTradeId);
                                    wp.AppendLog("Trade two factor confirmation was successfully accepted");
                                }
                                catch (SteamException ex)
                                {
                                    wp.AppendLog(ex.Message);
                                    wp.AppendLog("Waiting 30 seconds before trying again");
                                    this.ConfirmTradeTransactions(numberTradeId);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Log.Debug("Error on trade offer send", ex);
                            wp.AppendLog($"Error on trade offer send - {ex.Message}");
                        }
                    },
                "Trade send");
        }

        private void ProcessMarketSellInventoryPage(
            ObservableCollection<MarketSellModel> marketSellItems,
            InventoryRootModel inventoryPage)
        {
            var items = this.Inventory.ProcessInventoryPage(inventoryPage).ToArray();

            items = this.Inventory.FilterInventory(items, true, false);

            var groupedItems = items.GroupBy(i => i.Description.MarketHashName).ToArray();

            foreach (var group in groupedItems)
            {
                var existModel = marketSellItems.FirstOrDefault(
                    item => item.ItemModel.Description.MarketHashName == group.Key);

                if (existModel != null)
                {
                    foreach (var groupItem in group.ToArray())
                    {
                        existModel.ItemsList.Add(groupItem);
                    }

                    existModel.RefreshCount();
                }
                else
                {
                    marketSellItems.AddDispatch(new MarketSellModel(group.ToArray()));
                }
            }
        }

        private void ProcessMarketSellListingsPage(
            SellListingsPage sellListingsPage,
            ObservableCollection<MarketRelistModel> marketSellListings)
        {
            var groupedItems = sellListingsPage.SellListings.ToArray().GroupBy(x => new { x.HashName, x.Price });

            foreach (var group in groupedItems)
            {
                var existModel = marketSellListings.FirstOrDefault(
                    item => item.ItemModel.HashName == group.Key.HashName && item.ItemModel.Price == group.Key.Price);

                if (existModel != null)
                {
                    foreach (var groupItem in group.ToArray())
                    {
                        existModel.ItemsList.Add(groupItem);
                    }

                    existModel.RefreshCount();
                }
                else
                {
                    marketSellListings.AddDispatch(new MarketRelistModel(group.ToArray()));
                }
            }
        }

        private void ProcessTradeSendInventoryPage(
            ObservableCollection<SteamItemsModel> marketSellItems,
            InventoryRootModel inventoryPage,
            bool onlyUnmarketable)
        {
            var items = this.Inventory.ProcessInventoryPage(inventoryPage).ToArray();

            items = this.Inventory.FilterInventory(items, false, true);

            if (onlyUnmarketable)
            {
                items = items.Where(i => i.Description.IsMarketable == false).ToArray();
            }

            var groupedItems = items.GroupBy(i => i.Description.MarketHashName).ToArray();

            foreach (var group in groupedItems)
            {
                var existModel = marketSellItems.FirstOrDefault(
                    item => item.ItemModel.Description.MarketHashName == group.Key);

                if (existModel != null)
                {
                    foreach (var groupItem in group.ToArray())
                    {
                        existModel.ItemsList.Add(groupItem);
                    }

                    existModel.RefreshCount();
                }
                else
                {
                    marketSellItems.AddDispatch(new SteamItemsModel(group.ToArray()));
                }
            }
        }
    }
}