﻿namespace SteamAutoMarket.WorkingProcess.PriceLoader
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Windows.Forms;

    using SteamAutoMarket.CustomElements.Utils;
    using SteamAutoMarket.Utils;
    using SteamAutoMarket.WorkingProcess.Settings;

    internal class PriceLoader
    {
        private static readonly Queue<PriceLoadTask> WorkingTasksQueue = new Queue<PriceLoadTask>();

        private static Thread workingThread = new Thread(ProcessPendingTasks);

        private static Semaphore semaphore = new Semaphore(
            SavedSettings.Get().PriceLoadingThreads,
            SavedSettings.Get().PriceLoadingThreads);

        private static bool isForced;

        private static DataGridView allItemsGrid;

        private static AllItemsListGridUtils allItemsListGridUtils;

        private static DataGridView itemsToSaleGrid;

        private static DataGridView relistGridView;

        public static PricesCache AveragePricesCache { get; } = new PricesCache(
            "average_prices_cache.ini",
            SavedSettings.Get().SettingsHoursToBecomeOldAveragePrice);

        public static PricesCache CurrentPricesCache { get; } = new PricesCache(
            "current_prices_cache.ini",
            SavedSettings.Get().SettingsHoursToBecomeOldCurrentPrice);

        public static void Init(DataGridView table, ETableToLoad tableToLoad)
        {
            switch (tableToLoad)
            {
                case ETableToLoad.AllItemsTable:
                    allItemsGrid = table;
                    allItemsListGridUtils = new AllItemsListGridUtils(table);
                    break;

                case ETableToLoad.ItemsToSaleTable:
                    itemsToSaleGrid = table;
                    break;

                case ETableToLoad.RelistTable:
                    relistGridView = table;
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(tableToLoad), tableToLoad, null);
            }
        }

        public static void StopAll()
        {
            WorkingTasksQueue.Clear();
        }

        public static void WaitForLoadFinish()
        {
            try
            {
                if (workingThread.IsAlive)
                {
                    workingThread.Join();
                }
            }
            catch
            {
                // ignored
            }
        }

        public static void StartPriceLoading(ETableToLoad tableToLoad, bool force = false)
        {
            isForced = force;
            if (isForced)
            {
                ClearAllPriceCells(tableToLoad);
            }

            switch (tableToLoad)
            {
                case ETableToLoad.AllItemsTable:
                    {
                        AddAllItemsToSaleTasksToQueue();
                        StartWorkingThread();
                        break;
                    }

                case ETableToLoad.ItemsToSaleTable:
                    {
                        WorkingTasksQueue.Clear();
                        AddItemsToSaleTasksToQueue();
                        AddAllItemsToSaleTasksToQueue();

                        StartWorkingThread();
                        break;
                    }

                case ETableToLoad.RelistTable:
                    {
                        // todo
                        break;
                    }

                default:
                    throw new ArgumentOutOfRangeException(nameof(tableToLoad), tableToLoad, null);
            }
        }

        private static void ProcessPendingTasks()
        {
            while (WorkingTasksQueue.Count > 0)
            {
                try
                {
                    semaphore.WaitOne();
                    var priceLoadTask = WorkingTasksQueue.Dequeue();

                    Task.Run(
                        () =>
                            {
                                if (priceLoadTask.Task.Status == TaskStatus.Created)
                                {
                                    priceLoadTask.Task.Start();
                                    Task.WaitAll(priceLoadTask.Task);
                                }

                                semaphore.Release();
                            });
                }
                catch (Exception ex)
                {
                    if (ex is SemaphoreFullException || ex is InvalidOperationException)
                    {
                        continue;
                    }
                }
            }
        }

        private static void StartWorkingThread()
        {
            if (workingThread.ThreadState == ThreadState.Running)
            {
                return;
            }

            semaphore = new Semaphore(SavedSettings.Get().PriceLoadingThreads, SavedSettings.Get().PriceLoadingThreads);
            workingThread = new Thread(ProcessPendingTasks);
            workingThread.Start();
        }

        private static void ClearAllPriceCells(ETableToLoad tableToLoad)
        {
            DataGridViewTextBoxCell cell;

            switch (tableToLoad)
            {
                case ETableToLoad.AllItemsTable:
                    {
                        foreach (var row in allItemsGrid.Rows.Cast<DataGridViewRow>())
                        {
                            cell = allItemsListGridUtils.GetGridCurrentPriceTextBoxCell(row.Index).Cell;
                            if (cell != null)
                            {
                                cell.Value = null;
                            }

                            cell = allItemsListGridUtils.GetGridAveragePriceTextBoxCell(row.Index).Cell;
                            if (cell != null)
                            {
                                cell.Value = null;
                            }
                        }

                        break;
                    }

                case ETableToLoad.ItemsToSaleTable:
                    foreach (var row in itemsToSaleGrid.Rows.Cast<DataGridViewRow>())
                    {
                        cell = ItemsToSaleGridUtils.GetGridCurrentPriceTextBoxCell(itemsToSaleGrid, row.Index);
                        if (cell != null)
                        {
                            cell.Value = null;
                        }

                        cell = ItemsToSaleGridUtils.GetGridAveragePriceTextBoxCell(itemsToSaleGrid, row.Index);
                        if (cell != null)
                        {
                            cell.Value = null;
                        }
                    }

                    break;

                case ETableToLoad.RelistTable:
                    // todo
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(tableToLoad), tableToLoad, null);
            }
        }

        #region ALL ITEMS

        private static void AddAllItemsToSaleTasksToQueue()
        {
            var rows = GetAllItemsRowsWithNoPrice();
            var tasks = rows.Select(row => new Task(async () => await SetAllItemsListGridValues(row))).ToList();
            tasks.ForEach(t => WorkingTasksQueue.Enqueue(new PriceLoadTask(ETableToLoad.AllItemsTable, t)));
        }

        private static async Task SetAllItemsListGridValues(DataGridViewRow row)
        {
            try
            {
                var currentPriceCell = allItemsListGridUtils.GetGridCurrentPriceTextBoxCell(row.Index).Cell;
                var averagePriceCell = allItemsListGridUtils.GetGridAveragePriceTextBoxCell(row.Index).Cell;

                var prices = await GetAllItemsRowPrice(row);

                var currentPrice = prices.Item1;
                if (currentPrice != null && currentPrice != -1)
                {
                    currentPriceCell.Value = currentPrice;
                }

                var averagePrice = prices.Item2;
                if (averagePrice != null && averagePrice != -1)
                {
                    averagePriceCell.Value = averagePrice;
                }
            }
            catch (Exception ex)
            {
                Logger.Warning("Error on parsing item price", ex);
            }
        }

        private static IEnumerable<DataGridViewRow> GetAllItemsRowsWithNoPrice()
        {
            return allItemsGrid.Rows.Cast<DataGridViewRow>().Where(
                r => allItemsListGridUtils.GetGridCurrentPriceTextBoxCell(r.Index).Value.Equals(null)
                     || allItemsListGridUtils.GetGridAveragePriceTextBoxCell(r.Index).Value.Equals(null));
        }

        private static async Task<Tuple<double?, double?>> GetAllItemsRowPrice(DataGridViewRow row)
        {
            try
            {
                var itemsCell = allItemsListGridUtils.GetGridHiddenItemsListCell(row.Index);
                if (itemsCell?.Cell == null)
                {
                    return new Tuple<double?, double?>(-1, -1);
                }

                var item = itemsCell.Value?.ItemsList?.FirstOrDefault();
                if (item == null)
                {
                    return new Tuple<double?, double?>(-1, -1);
                }

                double? currentPrice = null;
                if (isForced == false)
                {
                    var cachedCurrentPrice = CurrentPricesCache.Get(item);
                    if (cachedCurrentPrice != null)
                    {
                        currentPrice = cachedCurrentPrice.Price;
                    }
                }

                if (currentPrice == null)
                {
                    currentPrice = await CurrentSession.SteamManager.GetCurrentPrice(item.Asset, item.Description);
                    if (currentPrice != null && currentPrice > 0)
                    {
                        CurrentPricesCache.Cache(item.Description.MarketHashName, currentPrice.Value);
                    }
                }

                double? averagePrice = null;
                if (isForced == false)
                {
                    var cachedCurrentPrice = AveragePricesCache.Get(item);
                    if (cachedCurrentPrice != null)
                    {
                        averagePrice = cachedCurrentPrice.Price;
                    }
                }

                if (averagePrice != null)
                {
                    return new Tuple<double?, double?>(currentPrice, averagePrice);
                }

                averagePrice = CurrentSession.SteamManager.GetAveragePrice(
                    item.Asset,
                    item.Description,
                    SavedSettings.Get().SettingsAveragePriceParseDays);

                if (averagePrice != null && (double)averagePrice > 0)
                {
                    AveragePricesCache.Cache(item.Description.MarketHashName, averagePrice.Value);
                }

                return new Tuple<double?, double?>(currentPrice, averagePrice);
            }
            catch (Exception ex)
            {
                Logger.Warning($"Error on parsing row price - {ex.Message}");
                return new Tuple<double?, double?>(0, 0);
            }
        }

        #endregion

        #region ITEMS TO SALE

        private static void AddItemsToSaleTasksToQueue()
        {
            var rows = GetItemsToSaleRowsWithNoPrice();
            var tasks = rows.Select(row => new Task(async () => await SetItemToSaleRowCurrentPrices(row))).ToList();
            tasks.ForEach(t => WorkingTasksQueue.Enqueue(new PriceLoadTask(ETableToLoad.ItemsToSaleTable, t)));
        }

        private static async Task SetItemToSaleRowCurrentPrices(DataGridViewRow row)
        {
            try
            {
                var currentPriceCell = ItemsToSaleGridUtils.GetGridCurrentPriceTextBoxCell(itemsToSaleGrid, row.Index);
                var averagePriceCell = ItemsToSaleGridUtils.GetGridAveragePriceTextBoxCell(itemsToSaleGrid, row.Index);

                var prices = await GetItemsToSaleRowPrice(row);

                var currentPrice = prices.Item1;
                if (currentPrice != -1)
                {
                    currentPriceCell.Value = currentPrice;
                }

                var averagePrice = prices.Item2;
                if (averagePrice != -1)
                {
                    averagePriceCell.Value = averagePrice;
                }
            }
            catch (Exception ex)
            {
                Logger.Warning("Error on parsing item price", ex);
            }
        }

        private static IEnumerable<DataGridViewRow> GetItemsToSaleRowsWithNoPrice()
        {
            return itemsToSaleGrid.Rows.Cast<DataGridViewRow>().Where(
                r => ItemsToSaleGridUtils.GetRowItemPrice(itemsToSaleGrid, r.Index).Equals(null)
                     || ItemsToSaleGridUtils.GetRowAveragePrice(itemsToSaleGrid, r.Index).Equals(null));
        }

        private static async Task<Tuple<double?, double?>> GetItemsToSaleRowPrice(DataGridViewRow row)
        {
            try
            {
                var itemsCell = ItemsToSaleGridUtils.GetGridHidenItemsListCell(itemsToSaleGrid, row.Index);
                if (itemsCell == null || itemsCell.Value == null)
                {
                    return new Tuple<double?, double?>(-1, -1);
                }

                var item = ItemsToSaleGridUtils.GetRowItemsList(itemsToSaleGrid, row.Index).FirstOrDefault();
                if (item == null)
                {
                    return new Tuple<double?, double?>(-1, -1);
                }

                double? currentPrice = null;
                if (isForced == false)
                {
                    var cachedCurrentPrice = CurrentPricesCache.Get(item);
                    if (cachedCurrentPrice != null)
                    {
                        currentPrice = cachedCurrentPrice.Price;
                    }
                }

                if (currentPrice == null)
                {
                    currentPrice = await CurrentSession.SteamManager.GetCurrentPrice(item.Asset, item.Description);
                    if (currentPrice != null && currentPrice != 0)
                    {
                        CurrentPricesCache.Cache(item.Description.MarketHashName, currentPrice.Value);
                    }
                }

                double? averagePrice = null;
                if (isForced == false)
                {
                    var cachedCurrentPrice = AveragePricesCache.Get(item);
                    if (cachedCurrentPrice != null)
                    {
                        averagePrice = cachedCurrentPrice.Price;
                    }
                }

                if (averagePrice != null)
                {
                    return new Tuple<double?, double?>(currentPrice, averagePrice);
                }

                averagePrice = CurrentSession.SteamManager.GetAveragePrice(
                    item.Asset,
                    item.Description,
                    SavedSettings.Get().SettingsAveragePriceParseDays);

                if (averagePrice != null && averagePrice != 0)
                {
                    AveragePricesCache.Cache(item.Description.MarketHashName, averagePrice.Value);
                }

                return new Tuple<double?, double?>(currentPrice, averagePrice);
            }
            catch (Exception ex)
            {
                Logger.Debug($"Error on parsing item price - {ex.Message}");
                return new Tuple<double?, double?>(0, 0);
            }
        }

        #endregion
    }
}