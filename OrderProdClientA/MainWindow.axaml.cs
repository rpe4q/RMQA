using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Bogus;
using Microsoft.EntityFrameworkCore;
using Models;
using MsBox.Avalonia;
using Newtonsoft.Json;
using OrderProdClientA.Converters;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Serilog;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BoxEnum = MsBox.Avalonia.Enums;

namespace OrderProdClientA
{
    public partial class MainWindow : Window
    {
        private MainViewModel ViewModel { get; } = new();
        private RabbitMQProducer? _producer;
        private ILogger? _logger;
        private AppDbContext context;
        public string PlaceholderText { get; set; }
        static string message = "";
        delegate void AppendText(string text);
        private CancellationTokenSource cts = new CancellationTokenSource();
        static async Task multicastSend(CancellationToken token)
        {
            var factory = new ConnectionFactory { HostName = "localhost" };
            using var connection = await factory.CreateConnectionAsync();
            using var channel = await connection.CreateChannelAsync();

            await channel.QueueDeclareAsync(queue: "hello",
                            durable: false,
                            exclusive: false,
                            autoDelete: false,
                            arguments: null);
            try
            {
                var body = Encoding.UTF8.GetBytes(message);
                await channel.BasicPublishAsync(
                    exchange: string.Empty,
                    routingKey: "hello",
                    body: body);
                await Task.Delay(1000, token); // пауза 1 сек
            }
            catch (Exception ex)
            {
                // Логирование ошибки
                var WrongValBox = MessageBoxManager.GetMessageBoxStandard(
                "!!!", $"Ошибка чата!\n{ex.Message}",
                BoxEnum.ButtonEnum.Ok, BoxEnum.Icon.Error);
                await WrongValBox.ShowAsync();
                await Task.Delay(5000); // повтор через 5 сек
            }
        }
        async Task Listner(CancellationToken token)
        {
            var factory = new ConnectionFactory { HostName = "localhost" };
            using var connection = await factory.CreateConnectionAsync();
            using var channel = await connection.CreateChannelAsync();

            await channel.QueueDeclareAsync(queue: "hello",
                            durable: false,
                            exclusive: false,
                            autoDelete: false,
                            arguments: null);
            var consumer = new AsyncEventingBasicConsumer(channel);
            try
            {
                consumer.ReceivedAsync += (model, ea) =>
                {
                    var body = ea.Body.ToArray();
                    var message = Encoding.UTF8.GetString(body);
                    Dispatcher.UIThread.Invoke(() => AppendTextProc($"CoffeeMarket: {message}\r\n"));
                    return Task.CompletedTask;
                };
            }
            catch (Exception ex)
            {
                var MsgRecieveErrBox = MessageBoxManager.GetMessageBoxStandard(
                    "!!!", $"Ошибка обработки сообщения!\n{ex.Message}",
                    BoxEnum.ButtonEnum.Ok, BoxEnum.Icon.Error);
                await MsgRecieveErrBox.ShowAsync();
            }

            // Привязка потребителя к очереди
            await channel.BasicConsumeAsync(
                queue: "hello",
                autoAck: true,
                consumer: consumer
            );

            // Ожидание отмены
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(100, token);
            }
            void AppendTextProc(string text)
            {
                ChatBoxCli.Text = text;
            }
        }
        public MainWindow()
        {
            //AvaloniaXamlLoader.Load(this);
            //Resources.Add("BoolToBrushConverter", new BoolToBrushConverter());

            InitializeComponent();

            // в самом начале - потом Dispose
            context = new AppDbContext();
            context.Database.EnsureCreated();

            Task.Run(() => Listner(cts.Token));
            ViewModel.ProductNames = [
                "Latte", "Cappuccino", "Flat White", 
                "Mocha", "Frappe", "Turkish", 
                "Espresso", "Americano", "Raf"];
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

                //ViewModel.LoadOrders(orderMessages);
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
                _producer = new RabbitMQProducer();
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

        private void Send_Click(object sender, RoutedEventArgs e)
        {
            _ = SClickTask();
        }

        private async Task SClickTask()
        {
            if (_producer == null)
            {
                var ServNonInitBox = MessageBoxManager.GetMessageBoxStandard(
                    "!!!", "RabbitMQ не инициализирован\nПроверьте подключение к серверу!",
                    BoxEnum.ButtonEnum.Ok, BoxEnum.Icon.Warning);
                await ServNonInitBox.ShowAsync();
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
                Price = new Faker().Random.Decimal(10.0m, 450.99m)
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

        private void Hide_Click(object? sender, RoutedEventArgs e)
        {
            CCliWin.Hide();
        }

        private void ChatBoxCli_TextChanged(object? sender, TextChangedEventArgs e)
        {
            message = ChatBoxCli.Text;
        }

        private void CliMsgSendBtn_Click(object? sender, RoutedEventArgs e)
        {
            Task.Run(() => multicastSend(cts.Token));
        }
    }
}