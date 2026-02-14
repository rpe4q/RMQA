using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Net.Http;
using System.Text;
using System.Windows.Input;

namespace OrderProdClientA
{
    public partial class MainViewModel : ObservableObject
    {
        [ObservableProperty]
        private ObservableCollection<OrderMessage> orders = new();

        [ObservableProperty]
        private OrderMessage currentOrder;
        // тут тоже магия - автоматически будет Orders - далее он и используется

        //private User CurrentUser { get; set; } = new User
        //{
        //    Id = 1
        //};

        //private int UserId { get; set; } = 1;

        public string[] ProductNames { get; set; }


        partial void OnOrdersChanged(ObservableCollection<OrderMessage> value)
        {
            // Логика при изменении коллекции (опционально)
        }

        public void AddOrder(OrderMessage order)
        {
            Orders.Add(order);
        }

        public void LoadOrders(IEnumerable<OrderMessage> ordersList)
        {
            Orders.Clear();
            foreach (var order in ordersList)
                Orders.Add(order);
        }
    }
}
