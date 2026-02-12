using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Models;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace OrderConsServA
{
    public partial class MainViewModel : ObservableObject
    {
        [ObservableProperty]
        private ObservableCollection<OrderMessage> orders = new();

        public MainViewModel()
        {
            Orders = new ObservableCollection<OrderMessage>();
        }

        [RelayCommand]
        public void AddOrder(OrderMessage order)
        {
            Orders.Add(order);
        }

        public void LoadOrders(List<OrderMessage> orders)
        {
            Orders.Clear();
            foreach (var order in orders)
            {
                Orders.Add(order);
            }
        }
    }
}
