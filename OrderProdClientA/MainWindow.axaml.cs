using Avalonia.Controls;
using Avalonia.Interactivity;
using MsBox.Avalonia;
using BoxEnum = MsBox.Avalonia.Enums;
using Bogus;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Models;
using RabbitMQ.Client;
using Serilog;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Linq;

namespace OrderProdClientA
{
    public partial class MainWindow : Window
    {
        private MainViewModel ViewModel { get; } = new();
        private RabbitMQProducer? _producer;
        private ILogger? _logger;
        private AppDbContext context;
        public string PlaceholderText { get; set; }
        public MainWindow()
        {
            InitializeComponent();

            // в самом начале - потом Dispose
            context = new AppDbContext();
            context.Database.EnsureCreated();

            ViewModel.ProductNames = ["Latte", "Cappuccino", "Flat White", "Mocha", "Frappe", "Turkish", "Espresso", "Americano", "Raf"];
            this.DataContext = ViewModel;

            // загрузка данных и привязка
            LoadData();

            // проверка каталога logs и создание файла с текущей датой
            var logsDir = "logs";
            if (!Directory.Exists(logsDir))
                Directory.CreateDirectory(logsDir);
            var logFilePath = Path.Combine("logs", $"consumer-{DateTime.Today:yyyy-MM-dd}.log");

            _logger = new LoggerConfiguration()
                .WriteTo.Console()
                .WriteTo.File(logFilePath)
                .CreateLogger();

            // Используем Loaded для асинхронной инициализации
            // иначе м.б. deadlock с UI
            this.Loaded += async (sender, e) => await InitializeRabbitMQAsync();
        }

        private async Task LoadData()
        {
            try
            {
                // Include для загрузки связанных Customer и Product
                var orders = context.Orders
                    .Include(o => o.Customer)
                    .Include(o => o.Product)
                    .ToList();

                var orderMessages = orders.Select(o => new OrderMessage
                {
                    CustomerName = o.Customer?.Name ?? "Неизвестный",
                    ProductName = o.Product?.Name ?? "Неизвестный",
                    Price = o.Product?.Price ?? 0m,
                    Quantity = o.Quantity
                }).ToList();

                _logger?.Information("Загружено {Count} заказов из БД", orderMessages.Count);

                ViewModel.LoadOrders(orderMessages);
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Ошибка при загрузке данных из БД");
                var DataLoadErrBox = MessageBoxManager.GetMessageBoxStandard(
                    "Ошибка", $"Ошибка загрузки данных: {ex.Message}",
                    BoxEnum.ButtonEnum.Ok, BoxEnum.Icon.Error);
                await DataLoadErrBox.ShowAsync();
            }
        }

        private async Task InitializeRabbitMQAsync()
        {
            try
            {
                _producer = new RabbitMQProducer(_logger);
                await _producer.InitializeAsync();
                _logger.Information("Подключение к RabbitMQ установлено");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Ошибка подключения к RabbitMQ");
                var ConnErrBox = MessageBoxManager.GetMessageBoxStandard(
                    "Ошибка подключения",
                    $"Не удалось подключиться к RabbitMQ!\nОшибка: {ex.Message}",
                    BoxEnum.ButtonEnum.Ok, BoxEnum.Icon.Error);
                await ConnErrBox.ShowAsync();
            }
        }
        private void Random_Click(object sender, RoutedEventArgs e)
        {
            // Генератор клиентов
            var customerFaker = new Faker("ru")
                .Person
                .FirstName;

            // Генерация заказа
            txtCustomer.Text = customerFaker;
            txtQuantity.Text = new Faker().Random.Int(1, 10).ToString();
        }

        private async void Send_Click(object sender, RoutedEventArgs e)
        {
            if (_producer == null)
            {
                var ServNonInitBox = MessageBoxManager.GetMessageBoxStandard(
                    "!!!", "RabbitMQ не инициализирован\nПроверьте подключение к серверу!",
                    BoxEnum.ButtonEnum.Ok, BoxEnum.Icon.Warning);
                await ServNonInitBox.ShowAsync();
                return;
            }

            var customer = txtCustomer.Text.Trim();
            var product = txtProduct.Text.Trim();
            
            if (customer == "" || customer == null)
            {
                var NoNameBox = MessageBoxManager.GetMessageBoxStandard(
                    "!!!", "Введите имя!",
                    BoxEnum.ButtonEnum.Ok, BoxEnum.Icon.Warning);
                await NoNameBox.ShowAsync();
                return;
            }

            if (product == "" || product == null)
            {
                var NoProductBox = MessageBoxManager.GetMessageBoxStandard(
                    "!!!", "Выберите продукт!",
                    BoxEnum.ButtonEnum.Ok, BoxEnum.Icon.Warning);
                await NoProductBox.ShowAsync();
                return;
            }


            if (!int.TryParse(txtQuantity.Text.Trim(), out int quantity) || quantity <= 0)
            {
                var WrongValBox = MessageBoxManager.GetMessageBoxStandard(
                    "Введите корректное количество!", "Введите корректное количество!",
                    BoxEnum.ButtonEnum.Ok, BoxEnum.Icon.Error);
                await WrongValBox.ShowAsync();
                return;
            }


            var message = new OrderMessage
            {
                CustomerName = customer,
                ProductName = product,
                Quantity = quantity,
                Price = new Faker().Random.Decimal(10, 450)
                // OrderDate не надо - по умолчанию текущая
            };

            // добавляем в коллекцию для DataGrid
            //ViewModel.AddOrder(message);

            ViewModel.CurrentOrder = message;

            try
            {
                await _producer.SendOrderMessage(message);
                _logger.Information("Заказ отправлен: {@Order}", message);
                var OrderCompleteBox = MessageBoxManager.GetMessageBoxStandard(
                    "!!!", "Заказ успешно отправлен в очередь!",
                    BoxEnum.ButtonEnum.Ok, BoxEnum.Icon.Success);
                await OrderCompleteBox.ShowAsync();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Ошибка при отправке заказа");
                var OrderErrorBox = MessageBoxManager.GetMessageBoxStandard(
                    "!!!", $"Ошибка при отправке заказа: {ex.Message}",
                    BoxEnum.ButtonEnum.Ok, BoxEnum.Icon.Error);
                await OrderErrorBox.ShowAsync();
            }
        }
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            context.Dispose();
        }
        protected override void OnClosing(WindowClosingEventArgs e)
        {
            this.Hide();
            e.Cancel = true;
        }
    }
    public class RabbitMQProducer
    {
        private IConnection? _connection;
        private IChannel? _channel;
        private readonly ILogger? _logger;
        public RabbitMQProducer(ILogger logger)
        {
            _logger = logger;
        }

        public async Task InitializeAsync()
        {
            try
            {
                var factory = new ConnectionFactory { HostName = "localhost" };
                _connection = await factory.CreateConnectionAsync();
                _channel = await _connection.CreateChannelAsync();

                await _channel.QueueDeclareAsync(
                    queue: "order_queue",
                    durable: false,
                    exclusive: false,
                    autoDelete: false,
                    arguments: null);

                _logger.Information("Канал создан и очередь 'order_queue' объявлена");
            }
            catch (Exception ex)
            {
                _logger.Fatal(ex, "Ошибка при инициализации RabbitMQ");
                throw;
            }
        }
        public async Task SendOrderMessage(OrderMessage message)
        {
            if (_channel is not { IsOpen: true })
            {
                _logger.Error("Попытка отправить сообщение, но канал закрыт");
                throw new InvalidOperationException("Канал RabbitMQ не открыт");
            }

            try
            {
                var json = JsonConvert.SerializeObject(message);
                var body = Encoding.UTF8.GetBytes(json);

                await _channel.BasicPublishAsync(
                    exchange: "",
                    routingKey: "order_queue",
                    body: body);

                _logger.Information($"Заказ {json} отправлен в order_queue :-)");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Ошибка при публикации сообщения");
                throw;
            }
        }
    }
}