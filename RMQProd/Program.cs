using Models;
using Newtonsoft.Json;
using RabbitMQ.Client;
using RMQProd;
using System.Runtime;
using System.Text;

public class RabbitMQProducer
{
    private IConnection? _connection;
    private IChannel? _channel;
    private readonly RabbitMQSettings _settings;
    //private readonly ILogger? _logger;
    //public RabbitMQProducer(ILogger logger)
    //{
    //    _logger = logger;
    //}

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

            //_logger.Information("Канал создан и очередь 'order_queue' объявлена");
        }
        catch (Exception ex)
        {
            throw ex.InnerException;
        }
    }
    public async Task SendOrderMessage(OrderMessage message)
    {
        if (_channel is not { IsOpen: true })
        {
            //_logger.Error("Попытка отправить сообщение, но канал закрыт");
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

            //_logger.Information($"Заказ {json} отправлен в order_queue :-)");
        }
        catch (Exception ex)
        {
            //_logger.Error(ex, "Ошибка при публикации сообщения");
            throw;
        }
    }
    public async Task SendDeleteMessage(DeleteMessage message)
    {
        // Отправляем сообщение в специальную очередь для удалений
        await SendMessageAsync(message, _settings.DeleteQueueName);
    }

    // Общий метод для отправки сообщений
    private async Task SendMessageAsync(object message, string routingKey)
    {
        if (_channel is not { IsOpen: true })
        {
            throw new InvalidOperationException("Канал RabbitMQ не открыт");
        }

        try
        {
            var json = JsonConvert.SerializeObject(message);
            var body = Encoding.UTF8.GetBytes(json);


            await _channel.BasicPublishAsync(
                exchange: "",
                routingKey: routingKey,
                body: body);
        }
        catch (Exception ex)
        {
            // Логирование ошибки
            throw;
        }
    }

    public class DeleteMessage
    {
        public int Id { get; set; }
        public string CustomerName { get; set; }
        public string Type { get; set; } = "DELETE";
    }
}

public class Program
{
    static void Main()
    {
        RabbitMQProducer prd = new();
        Task.Run(prd.InitializeAsync);
    }
}