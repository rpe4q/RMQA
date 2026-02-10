using Avalonia.Controls;
using Avalonia.Interactivity;
using MsBox.Avalonia;
using Enu = MsBox.Avalonia.Enums;
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

namespace OrderProducerAvalonia
{
    public partial class MainWindow : Window
    {
        private MainViewModel ViewModel { get; } = new();
        private RabbitMQProducer? _producer;
        private ILogger? _logger;
        private AppDbContext context;
        public MainWindow()
        {
            InitializeComponent();

            // в самом начале - потом Dispose
            context = new AppDbContext();
            context.Database.EnsureCreated();

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

        private void LoadData()
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
                MessageBoxManager.GetMessageBoxStandard($"Ошибка загрузки данных: {ex.Message}", "Ошибка",
                    Enu.ButtonEnum.Ok, Enu.Icon.Error);
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
                MessageBoxManager.GetMessageBoxStandard(
                    $"Не удалось подключиться к RabbitMQ!\nОшибка: {ex.Message}",
                    "Ошибка подключения",
                    Enu.ButtonEnum.Ok, Enu.Icon.Error);
            }
        }
        private void Random_Click(object sender, RoutedEventArgs e)
        {
            // Генератор клиентов
            var customerFaker = new Faker("ru")
                .Person
                .FirstName;
            // Генератор продуктов
            var productNames = new[] { "Latte", "Cappuccino", "Flat White", "Mocha", "Frappe", "Turkish", "Espresso", "Americano", "Raf" };
            var productFaker = new Faker()
                .PickRandom(productNames);
            // Генерация заказа
            txtCustomer.Text = customerFaker;
            txtProduct.Text = productFaker;
            txtQuantity.Text = new Faker().Random.Int(1, 10).ToString();
        }

        private async void Send_Click(object sender, RoutedEventArgs e)
        {
            if (_producer == null)
            {
                MessageBoxManager.GetMessageBoxStandard("!!!", "RabbitMQ не инициализирован\nПроверьте подключение к серверу!");
                return;
            }

            var customer = txtCustomer.Text.Trim();
            var product = txtProduct.Text.Trim();
            if (!int.TryParse(txtQuantity.Text.Trim(), out int quantity) || quantity <= 0)
            {
                MessageBoxManager.GetMessageBoxStandard("Введите корректное количество!", "Введите корректное количество!");
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
            ViewModel.AddOrder(message);

            try
            {
                await _producer.SendOrderMessage(message);
                _logger.Information("Заказ отправлен: {@Order}", message);
                MessageBoxManager.GetMessageBoxStandard("!!!", "Заказ успешно отправлен в очередь!");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Ошибка при отправке заказа");
                MessageBoxManager.GetMessageBoxStandard("!!!", $"Ошибка при отправке заказа: {ex.Message}");
            }
        }
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            context.Dispose();
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