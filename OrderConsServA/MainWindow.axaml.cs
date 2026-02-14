using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Models;
using MsBox.Avalonia;
using Newtonsoft.Json;
using OrderConsServ.Converters;
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

namespace OrderConsServA
{
    public partial class MainWindow : Window
    {
        internal MainViewModel ViewModel { get; } = new();
        private AppDbContext context;
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
                var MsgSendErrBox = MessageBoxManager.GetMessageBoxStandard(
                "!!!", $"Ошибка отправки сообщения!\n{ex.Message}",
                BoxEnum.ButtonEnum.Ok, BoxEnum.Icon.Error);
                await MsgSendErrBox.ShowAsync();
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
                    Dispatcher.UIThread.Invoke(() => AppendTextProc($"Client: {message}\r\n"));
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
                ChatBoxServ.Text = text;
            }
        }
        public MainWindow()
        {
            //AvaloniaXamlLoader.Load(this);
            //Resources.Add("BoolToBrushConverter", new BoolToBrushConverter());
            InitializeComponent();
            this.DataContext = ViewModel;
            _ = ServInit();
            context = new AppDbContext();
            context.Database.EnsureCreated();
            _ = LoadData();
            Task.Run(() => Listner(cts.Token));
        }
        public async Task ServInit()
        {
            var host = Host.CreateDefaultBuilder()
                .ConfigureServices((context, services) =>
                {
                    services.AddHostedService(sp => new Worker(this));
                })
                .Build();

            await host.RunAsync();
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

                //_logger?.Information("Загружено {Count} заказов из БД", orderMessages.Count);

                ViewModel.LoadOrders(orderMessages);
            }
            catch (Exception ex)
            {
                //_logger?.Error(ex, "Ошибка при загрузке данных из БД");
                var DataLoadErrBox = MessageBoxManager.GetMessageBoxStandard(
                    "Ошибка", $"Ошибка загрузки данных: {ex.Message}",
                    BoxEnum.ButtonEnum.Ok, BoxEnum.Icon.Error);
                await DataLoadErrBox.ShowAsync();
            }
        }

        private void ChatBoxServ_TextChanged(object? sender, TextChangedEventArgs e)
        {
            message = ChatBoxServ.Text;
        }

        private void SrvMsgSendBtn_Click(object? sender, RoutedEventArgs e)
        {
            Task.Run(() => multicastSend(cts.Token));
        }

        private void Hide_Click(object? sender, RoutedEventArgs e)
        {
            CSrvWin.Hide();
        }

        private void Random_Click(object? sender, RoutedEventArgs e)
        {
        }

        private void Send_Click(object? sender, RoutedEventArgs e)
        {
        }
    }
    public class Worker : IHostedService, IDisposable
    {
        private TextBox _cBox;
        private IConnection? _connection;
        private IChannel? _channel;
        private Serilog.ILogger _logger;
        private MainViewModel _ViewModel;
        private AppDbContext context;

        public Worker(MainWindow window)
        {
            _cBox = window.ConsoleBox;
            _ViewModel = window.ViewModel;
            // проверка каталога logs и создание файла с текущей датой
            var logsDir = "logs";
            if (!Directory.Exists(logsDir))
                Directory.CreateDirectory(logsDir);
            var logFilePath = Path.Combine("logs", $"consumer-{DateTime.Today:yyyy-MM-dd}.log");

            _logger = new LoggerConfiguration()
                .WriteTo.Console()
                .WriteTo.File(logFilePath)
                .CreateLogger();
            context = new AppDbContext();
            // Принудительно создаём БД и таблицы, если их нет
            context.Database.EnsureCreated();
        }

        private async void AppendLog(string message)
        {
            // Используем Dispatcher для безопасного обновления UI
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _cBox.Text += $"{message}{Environment.NewLine}";
            });
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                var factory = new ConnectionFactory { HostName = "localhost" };
                _connection = await factory.CreateConnectionAsync(cancellationToken);
                _channel = await _connection.CreateChannelAsync(options: null, cancellationToken: cancellationToken);

                await _channel.QueueDeclareAsync("order_queue", false, false, false, null);

                var consumer = new AsyncEventingBasicConsumer(_channel);
                consumer.ReceivedAsync += async (sender, ea) =>
                {
                    try
                    {
                        var json = Encoding.UTF8.GetString(ea.Body.ToArray());
                        _logger.Information($"Получен заказ: {json}");
                        try
                        {
                            var message = JsonConvert.DeserializeObject<OrderMessage>(json);
                            if (message != null)
                                await SaveOrderAsync(message, ea.CancellationToken);
                            else
                                AppendLog("Получено сообщение, но не удалось десериализовать");
                            _logger.Warning("Получено сообщение, но не удалось десериализовать");
                        }
                        catch (Exception ex)
                        {
                            AppendLog("Ошибка при обработке сообщения");
                            _logger.Error(ex, "Ошибка при обработке сообщения");
                        }
                    }
                    catch (Exception ex)
                    {
                        AppendLog($"Ошибка: {ex.Message}");
                        _logger.Error($"Ошибка: {ex.Message}");
                    }
                };

                await _channel.BasicConsumeAsync("order_queue", true, consumer);
                AppendLog("Consumer слушает очередь order_queue...");
                _logger.Information("Consumer слушает очередь order_queue...");
            }
            catch (Exception ex)
            {
                AppendLog($"Критическая ошибка при запуске Consumer\n{ex.Message}");
                _logger.Fatal(ex, "Критическая ошибка при запуске Consumer");
                throw;
            }
        }
        private async Task SaveOrderAsync(OrderMessage message, CancellationToken ct)
        {
            var customer = await context.Customers
                .FirstOrDefaultAsync(c => c.Name == message.CustomerName, ct);

            if (customer == null)
            {
                customer = new Customer
                {
                    Name = message.CustomerName,
                    Email = $"{message.CustomerName.Replace(" ", "").ToLower()}@yandex.ru"
                };
                context.Customers.Add(customer);
                await context.SaveChangesAsync(ct);
                AppendLog($"Новый клиент сохранён: {customer.Name}");
                _logger.Information("Новый клиент сохранён: {Name}", customer.Name);
            }

            var product = await context.Products
                .FirstOrDefaultAsync(p => p.Name == message.ProductName, ct);

            if (product == null)
            {
                product = new Product
                {
                    Name = message.ProductName,
                    Price = new Random().Next(100, 1000)
                };
                context.Products.Add(product);
                await context.SaveChangesAsync(ct);
                AppendLog($"Новый товар сохранён: {product.Name}");
                _logger.Information("Новый товар сохранён: {Name}", product.Name);
            }

            var order = new Order
            {
                CustomerId = customer.Id,
                ProductId = product.Id,
                Quantity = message.Quantity
            };

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _ViewModel.AddOrder(message);
            });

            context.Orders.Add(order);
            await context.SaveChangesAsync(ct);
            AppendLog($"Заказ сохранён: ID={order.Id}, Клиент={customer.Name}, Товар={product.Name}");
            _logger.Information("Заказ сохранён: ID={OrderId}, Клиент={Customer}, Товар={Product}",
                    order.Id, customer.Name, product.Name);
        }
        public Task StopAsync(CancellationToken cancellationToken)
        {
            AppendLog("Остановка Consumer...");
            _logger.Information("Остановка Consumer...");
            return Task.CompletedTask;
        }
        public void Dispose()
        {
            AppendLog("Остановка канала и соединения...");
            _logger.Information("Остановка канала и соединения...");
            _channel?.DisposeAsync().GetAwaiter().GetResult();
            _connection?.DisposeAsync().GetAwaiter().GetResult();
            context.Dispose();
        }
    }
}