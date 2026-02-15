using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace OrderConsServA
{
    public partial class MainViewModel : ObservableObject
    {
        [ObservableProperty]
        private ObservableCollection<OrderMessage> orders = new();

        [ObservableProperty]
        private ObservableCollection<ChatMessage> chatMessages = new();

        [ObservableProperty]
        private string newMessageText;
        public ICommand SendMessageCommand => new RelayCommand(SendMessage);

        public string[] ProductNames { get; set; }

        public void AddMessage(string message, bool isUser = true)
        {
            ChatMessages.Add(new ChatMessage
            {
                Text = message,
                IsUser = isUser,
                Timestamp = DateTime.Now
            });
        }

        public void SendMessage()
        {
            if (!string.IsNullOrEmpty(NewMessageText))
            {
                AddMessage(NewMessageText);
                NewMessageText = "";
                // Здесь логика отправки сообщения на сервер
            }
        }

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
    public class ChatMessage
    {
        public string Text { get; set; }
        public bool IsUser { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
