using CommunityToolkit.Mvvm.ComponentModel;
using Models;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace OrderProdClientA
{
    public partial class MainViewModel : ObservableObject
    {
        [ObservableProperty]
        private ObservableCollection<OrderMessage> orders = new();
        // тут тоже магия - автоматически будет Orders - далее он и используется

        [ObservableProperty]
        private OrderMessage currentOrder;
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
