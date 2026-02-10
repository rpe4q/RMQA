using CommunityToolkit.Mvvm.ComponentModel;

namespace Models
{
    public partial class OrderMessage : ObservableObject // ⚠️ partial
    {
        [ObservableProperty]
        private string? customerName; // Приватное поле без { get; set; }

        [ObservableProperty]
        private string? productName;

        [ObservableProperty]
        private decimal price;

        [ObservableProperty]
        private int quantity;

        [ObservableProperty]
        private DateTime orderDate = DateTime.UtcNow; // Инициализация в поле

        public decimal TotalPrice => Price * Quantity;
    }
}