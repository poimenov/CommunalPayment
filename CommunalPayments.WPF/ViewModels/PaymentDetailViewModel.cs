﻿using CommunalPayments.Common;
using CommunalPayments.Common.Interfaces;
using GalaSoft.MvvmLight.CommandWpf;
using log4net;
using MvvmDialogs;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;

namespace CommunalPayments.WPF.ViewModels
{
    public class PaymentDetailViewModel : DockWindowViewModel
    {
        private IPayment _dataAccess;
        private IEnumerable<Rate> _actualRates;
        private IEnumerable<Service> _services;
        private bool _importInProcess;
        private readonly IDialogService _dialogService;
        private readonly INetRepository _netRepository;
        protected override Type GetGridItemType
        {
            get
            {
                return typeof(PaymentItem);
            }
        }
        public PaymentDetailViewModel(IDataAccess<Account> accounts, IDataAccess<Service> services, IPayment dataAccess, IDataAccess<Rate> rates,
            IDialogService dialogService, INetRepository netRepository, ILog logger) : base(logger)
        {
            this._dataAccess = dataAccess;
            this._actualRates = rates.ItemsList.Where(x => x.Enabled).ToList();
            this._services = services.ItemsList.Where(x => x.ErcId > 0).ToList();
            _columns.Add(new KeyValuePair<string, string>("Id", "Id"));
            _columns.Add(new KeyValuePair<string, string>("ServiceId", "Service.Name"));
            _columns.Add(new KeyValuePair<string, string>("PeriodFrom", "PeriodFrom"));
            _columns.Add(new KeyValuePair<string, string>("PeriodTo", "PeriodTo"));
            _columns.Add(new KeyValuePair<string, string>("LastIndication", "LastIndication"));
            _columns.Add(new KeyValuePair<string, string>("CurrentIndication", "CurrentIndication"));
            _columns.Add(new KeyValuePair<string, string>("Value", "Value"));
            _columns.Add(new KeyValuePair<string, string>("Amount", "Amount"));
            Accounts = new ObservableCollection<Account>(accounts.ItemsList);
            _importInProcess = false;
            _dialogService = dialogService;
            _netRepository = netRepository;
        }
        public override string ItemTypeName { get => App.ResGlobal.GetString("PaymentItem");}
        public void ChangedItem(PaymentItem item)
        {
            if (item.LastIndication.HasValue && item.CurrentIndication.HasValue)
            {
                item.Value = item.CurrentIndication.Value - item.LastIndication.Value;
                if (item.ServiceId == 4 || item.ServiceId == 5)//горячая или холодная вода
                {
                    //пересчитать канализацию 
                    //если нет строки с канализацией
                    if (!ItemsList.Any(x => x.ServiceId == 6))
                    {
                        //добавить строку с канализацией
                        ItemsList.Add(new PaymentItem()
                        {
                            ServiceId = 6,
                            PeriodFrom = DateTime.Today.AddMonths(-1).AddDays(1 - DateTime.Today.Day),
                            PeriodTo = DateTime.Today.AddDays(-DateTime.Today.Day)
                    });
                    }
                    var canal = ItemsList.First(x => x.ServiceId == 6);
                    if (!canal.Value.HasValue)
                    {
                        canal.Value = 0;
                    }
                    //при изменении хол. или гор. воды метод будет вызываться дважды
                    //потому что меняется расход канализации
                    //TODO: как-то коряво написано. надо потом переписать
                    if (item.ServiceId == 4 && ItemsList.Any(x => x.ServiceId == 5) && ItemsList.First(x => x.ServiceId == 5).Value.HasValue)
                    {
                        canal.Value = item.Value + ItemsList.First(x => x.ServiceId == 5).Value.Value;
                    }
                    else if (item.ServiceId == 5 && ItemsList.Any(x => x.ServiceId == 4) && ItemsList.First(x => x.ServiceId == 4).Value.HasValue)
                    {
                        canal.Value = item.Value + ItemsList.First(x => x.ServiceId == 4).Value.Value;
                    }
                    else
                    {
                        canal.Value = item.Value;
                    }
                    canal.Amount = calculationAmount(canal.Value.Value, canal.ServiceId);
                }
            }
            if (item.Value.HasValue)
            {
                item.Amount = calculationAmount(item.Value.Value, item.ServiceId);
            }
            SelectedPayment = SelectedPayment;
            SelectedItem = item;
        }
        private decimal calculationAmount(decimal value, int serviceId)
        {
            decimal retVal = 0;
            var rates = _actualRates.Where(x => x.ServiceId == serviceId).OrderByDescending(x => x.VolumeFrom).ToList();
            foreach (var rate in rates)
            {
                if (rate.VolumeFrom < value)
                {
                    retVal += rate.Value * (value - rate.VolumeFrom);
                    value = rate.VolumeFrom;
                }
            }
            return Math.Round(retVal, 2);
        }
        public ObservableCollection<Account> Accounts { get; private set; }
        #region SelectedPayment
        private Payment _selectedPayment;
        public Payment SelectedPayment
        {
            get
            {
                if (null == _selectedPayment)
                {
                    _selectedPayment = new Payment() { Enabled = true };
                    ItemsList = null;
                }
                _selectedPayment.AccountId = SelectedAccountId;
                if (null != ItemsList)
                {
                    _selectedPayment.PaymentItems = ItemsList.ToList();
                }
                return _selectedPayment;
            }
            set
            {
                if (null == value)
                {
                    _selectedPayment = new Payment() { Enabled = true };
                    ItemsList = null;
                    RaisePropertyChanged(() => SelectedPayment);
                }
                else
                {
                    ItemsList = null;
                    _selectedPayment = value;
                    RaisePropertyChanged(() => SelectedPayment);
                    ItemsList = new ObservableCollection<PaymentItem>(_selectedPayment.PaymentItems);
                }
            }
        }

        #endregion
        #region Selected Payment item
        private PaymentItem _selectedItem;
        public PaymentItem SelectedItem
        {
            get
            {
                return _selectedItem;
            }
            set
            {
                _selectedItem = value;
                RaisePropertyChanged(() => SelectedItem);
            }
        }
        #endregion
        #region Payment items list
        private ObservableCollection<PaymentItem> _items;
        public ObservableCollection<PaymentItem> ItemsList
        {
            get
            {
                return _items;
            }
            set
            {
                _items = value;
                RaisePropertyChanged(() => ItemsList);
            }
        }
        #endregion
        #region Selected Account
        public Account SelectedAccount
        {
            get;
            private set;
        }
        #endregion
        #region SelectedAccountId
        private int _selectedAccountId;
        public int SelectedAccountId
        {
            get
            {
                return _selectedAccountId;
            }
            set
            {
                if (_selectedAccountId != value)
                {
                    _selectedAccountId = value;
                    SelectedAccount = Accounts.FirstOrDefault(x => x.Id == value);
                    ItemsList = null;
                    //if new paymment
                    if (_selectedAccountId > 0 && SelectedPayment.Id == 0)
                    {                        
                        var lastPayment = _dataAccess.LastPayment(_selectedAccountId);
                        var payment = new Payment() { Enabled = true };
                        var lst = new List<PaymentItem>();
                        if (null != lastPayment && null !=lastPayment.PaymentItems)
                        {
                            foreach (var prev in lastPayment.PaymentItems)
                            {
                                var curr = new PaymentItem();
                                curr.Enabled = true;
                                curr.CurrentIndication = prev.CurrentIndication;
                                curr.LastIndication = prev.CurrentIndication;
                                curr.ServiceId = prev.ServiceId;
                                curr.Service = prev.Service;
                                curr.PeriodFrom = DateTime.Today.AddMonths(-1).AddDays(1 - DateTime.Today.Day);
                                curr.PeriodTo = DateTime.Today.AddDays(-DateTime.Today.Day);
                                payment.PaymentItems.Add(curr);
                            }                            
                        }
                        SelectedPayment = payment;
                    }
                    RaisePropertyChanged(() => SelectedAccountId);
                    RaisePropertyChanged(() => SelectedAccount);
                }
            }
        }
        #endregion
        #region SaveCmd
        public ICommand SaveCmd { get { return new RelayCommand(OnSave, () => (SelectedAccountId > 0)); } }
        private void OnSave()
        {
            List<Payment> payments = new List<Payment>();
            payments.Add(SelectedPayment);
            if (SelectedPayment.Id > 0)
            {
                _dataAccess.Update(payments);
            }
            else
            {
                _dataAccess.Create(payments);
            }
            OnCancel();
        }
        #endregion
        #region CancelCmd
        public ICommand CancelCmd { get { return new RelayCommand(OnCancel, () => (SelectedAccountId > 0)); } }
        private void OnCancel()
        {
            if (SelectedPayment != null && SelectedPayment.Id > 0)
            {
                SelectedPayment = _dataAccess.Get(SelectedPayment.Id);
            }
            else
            {
                SelectedPayment = null;
                SelectedAccountId = 0;
            }
            SelectedItem = null;
        }
        #endregion
        #region CreateCmd
        public ICommand CreateCmd { get { return new RelayCommand(OnCreate, () => (SelectedAccountId > 0)); } }
        private void OnCreate()
        {
            PaymentItem item = new PaymentItem() { Enabled = true, PeriodFrom = DateTime.Today.AddMonths(-1).AddDays(1 - DateTime.Today.Day), PeriodTo = DateTime.Today.AddDays(-DateTime.Today.Day) };
            ItemsList.Add(item);
            SelectedItem = item;
        }
        #endregion
        #region DeleteCmd
        public RelayCommand<object> DeleteCmd { get { return new RelayCommand<object>(OnDelete, obj => (obj != null && ItemsList.Count > 0), false); } }
        private void OnDelete(object obj)
        {
            PaymentItem item = (PaymentItem)obj;
            ItemsList.Remove(item);
        }
        #endregion
        #region DebtCmd
        public RelayCommand<object> DebtCmd { get { return new RelayCommand<object>(OnDebtCmd, obj => (obj != null && !_importInProcess), false); } }
        private async void OnDebtCmd(object obj)
        {
            var account = obj as Account;
            if (null == account)
            {
                return;
            }
            var loginViewModel = new LoginViewModel();

            var success = _dialogService.ShowDialog(this, loginViewModel);
            if (success == true)
            {
                _netRepository.Login = loginViewModel.Login;
                _netRepository.Password = loginViewModel.Password;
                _importInProcess = true;
                this.CanClose = false;
                try
                {
                    var debt = await _netRepository.GetDebt(account, DateTime.Today);
                    if (null == debt)
                    {
                        _dialogService.ShowMessageBox(this, "Login/Password is failed", "Warning", System.Windows.MessageBoxButton.OK);
                    }
                    else if (debt.DebtItems.Count() == 0)
                    {
                        _dialogService.ShowMessageBox(this, "The server returned an empty dataset", "There is no data", System.Windows.MessageBoxButton.OK);
                    }
                    else
                    {
                        var debtViewModel = new DebtsViewModel(debt, this._logger);
                        success = _dialogService.ShowDialog(this, debtViewModel);
                        if (success == true)
                        {
                            foreach (var item in ItemsList)
                            {
                                if (debt.DebtItems.Any(x => x.ServiceName.ToLower() == item.Service.Name.ToLower()))
                                {
                                    var debtItem = debt.DebtItems.First(x => x.ServiceName.ToLower() == item.Service.Name.ToLower());
                                    SetAmount(debtItem, item, debtViewModel.SelectedPayBy);
                                }
                            }
                            var itemsServNames = ItemsList.Select(x => x.Service.Name).ToList();
                            var debtServNames = debt.DebtItems.Select(x => x.ServiceName).ToList();
                            var servNames = debtServNames.Except(itemsServNames).ToList();
                            foreach (var serv in servNames)
                            {
                                var debtItem = debt.DebtItems.First(x => x.ServiceName.ToLower() == serv.ToLower());
                                if (_services.Any(x => x.Name.ToLower() == serv.ToLower()))
                                {
                                    var item = new PaymentItem();
                                    item.Enabled = true;
                                    item.Service = _services.First(x => x.Name.ToLower() == serv.ToLower());
                                    item.ServiceId = item.Service.Id;
                                    item.PeriodFrom = DateTime.Today.AddMonths(-1).AddDays(1 - DateTime.Today.Day);
                                    item.PeriodTo = DateTime.Today.AddDays(-DateTime.Today.Day);
                                    SetAmount(debtItem, item, debtViewModel.SelectedPayBy);
                                    ItemsList.Add(item);
                                }
                            }
                            ItemsList = new ObservableCollection<PaymentItem>(ItemsList);
                            RaisePropertyChanged(() => ItemsList);
                            RaisePropertyChanged(() => SelectedPayment);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _dialogService.ShowMessageBox(this, ex.Message, "Error", System.Windows.MessageBoxButton.OK);
                }
                finally
                {
                    _importInProcess = false;
                    this.CanClose = true;
                }
            }
        }
        private void SetAmount(DebtItem source, PaymentItem destination, DebtsViewModel.PayBy payBy)
        {
            switch (payBy)
            {
                case DebtsViewModel.PayBy.Credit:
                    if (source.Credited.HasValue)
                    {
                        destination.Amount = source.Credited.Value;
                    }
                    break;
                case DebtsViewModel.PayBy.Saldo:
                    if (source.Saldo.HasValue)
                    {
                        destination.Amount = source.Saldo.Value;
                    }
                    break;
            }
        }
        #endregion

    }
}
