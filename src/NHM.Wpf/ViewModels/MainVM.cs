﻿using System;
using NHM.Common;
using NHM.Wpf.ViewModels.Models;
using NiceHashMiner;
using NiceHashMiner.Configs;
using NiceHashMiner.Devices;
using NiceHashMiner.Mining;
using NiceHashMiner.Stats;
using NiceHashMiner.Switching;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;

namespace NHM.Wpf.ViewModels
{
    public class MainVM : BaseVM
    {
        private readonly Timer _updateTimer;

        private IEnumerable<DeviceData> _devices;
        public IEnumerable<DeviceData> Devices
        {
            get => _devices;
            set
            {
                _devices = value;
                OnPropertyChanged();
            }
        }

        private IEnumerable<MiningData> _miningDevs;
        public IEnumerable<MiningData> MiningDevs
        {
            get => _miningDevs;
            set
            {
                _miningDevs = value;
                OnPropertyChanged();
            }
        }

        public IReadOnlyList<string> ServiceLocations => StratumService.MiningLocationNames;

        public int ServiceLocationIndex
        {
            get => ConfigManager.GeneralConfig.ServiceLocation;
            set => ConfigManager.GeneralConfig.ServiceLocation = value;
        }

        public string BtcAddress
        {
            get => ConfigManager.GeneralConfig.BitcoinAddress;
            set => ConfigManager.GeneralConfig.BitcoinAddress = value;
        }

        public string WorkerName
        {
            get => ConfigManager.GeneralConfig.WorkerName;
            set => ConfigManager.GeneralConfig.WorkerName = value;
        }

        public MiningState State => MiningState.Instance;

        private string PerTime => $"/{TimeFactor.UnitType}";

        private string _currency = ExchangeRateApi.ActiveDisplayCurrency;

        public string Currency
        {
            get => _currency;
            set
            {
                _currency = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CurrencyPerTime));
                OnPropertyChanged(nameof(ProfitPerTime));
            }
        }

        public string CurrencyPerTime => $"{Currency}{PerTime}";

        public string BtcPerTime => $"BTC{PerTime}";

        public string MBtcPerTime => $"m{BtcPerTime}";

        public string ProfitPerTime => $"Profit ({CurrencyPerTime})";

        public double GlobalRate => MiningDevs?.Sum(d => d.Payrate) ?? 0;

        public double GlobalRateFiat => MiningDevs?.Sum(d => d.FiatPayrate) ?? 0;

        private double _btcBalance;
        public double BtcBalance
        {
            get => _btcBalance;
            set
            {
                _btcBalance = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(FiatBalance));
            }
        }

        public double FiatBalance => ExchangeRateApi.ConvertFromBtc(BtcBalance);

        public MainVM()
        {
            _updateTimer = new Timer(1000);
            _updateTimer.Elapsed += UpdateTimerOnElapsed;

            ExchangeRateApi.CurrencyChanged += (_, curr) =>
            {
                Currency = curr;
                OnPropertyChanged(nameof(FiatBalance));
            };

            ApplicationStateManager.DisplayBTCBalance += UpdateBalance;
        }

        // TODO I don't like this way, a global refresh and notify would be better
        private void UpdateTimerOnElapsed(object sender, ElapsedEventArgs e)
        {
            if (Devices == null) return;
            foreach (var dev in Devices)
            {
                dev.RefreshDiag();
            }
        }

        public async Task InitializeNhm(IStartupLoader sl)
        {
            await ApplicationStateManager.InitializeManagersAndMiners(sl);

            Devices = new ObservableCollection<DeviceData>(AvailableDevices.Devices.Select(d => (DeviceData) d));
            MiningDevs = new ObservableCollection<MiningData>(AvailableDevices.Devices.Select(d => new MiningData(d)));

            MiningStats.DevicesMiningStats.CollectionChanged += DevicesMiningStatsOnCollectionChanged;

            _updateTimer.Start();

            // TODO auto-start mining
        }

        private void DevicesMiningStatsOnCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                case NotifyCollectionChangedAction.Replace:
                    foreach (var stat in e.NewItems.OfType<MiningStats.DeviceMiningStats>())
                    {
                        var miningDev = MiningDevs.FirstOrDefault(d => d.Dev.Uuid == stat.DeviceUUID);
                        if (miningDev != null) miningDev.Stats = stat;
                    }

                    break;
                case NotifyCollectionChangedAction.Reset:
                    foreach (var miningDev in MiningDevs)
                    {
                        miningDev.Stats = null;
                    }

                    break;
            }

            OnPropertyChanged(nameof(GlobalRate));
            OnPropertyChanged(nameof(GlobalRateFiat));
        }

        private void UpdateBalance(object sender, double btcBalance)
        {
            BtcBalance = btcBalance;
        }

        public async Task StartMining()
        {
            if (!await NHSmaData.WaitOnDataAsync(10)) return;

            // TODO there is a mess of blocking and not-awaited async code down the line, 
            // Just wrapping with Task.Run here for now

            await Task.Run(() => { ApplicationStateManager.StartAllAvailableDevices(); });
        }

        public async Task StopMining()
        {
            await Task.Run(() => { ApplicationStateManager.StopAllDevice(); });
        }
    }
}