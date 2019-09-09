﻿using Armsoft.Sandbox.InteractiveMessageBroker.Common;
using Armsoft.Sandbox.InteractiveMessageBroker.Common.InternalMessages;
using Armsoft.Sandbox.InteractiveMessageBroker.MessageHandling;
using Microsoft.ServiceBus;
using Microsoft.ServiceBus.Messaging;
using Newtonsoft.Json;
using NLog;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Armsoft.Sandbox.InteractiveMessageBroker.Subscribers
{
    public class OutboundQueueSubscriber : MessageSubscriber
    {
        private const string MessageTypeKey = "messageType";

        private readonly ILogger _logger = LogManager.GetCurrentClassLogger();
        private readonly IOutboundMessageHandler _messageHandler;

        public OutboundQueueSubscriber(MessagingFactory messagingFactory, NamespaceManager namespaceManager, IOutboundMessageHandler messageHandler)
            : base(Guid.Parse("700ef2ac-989c-4c37-a6bc-e70ba8e71b72"), QueueNames.Outbound, messagingFactory, namespaceManager)
        {
            _messageHandler = messageHandler;
        }

        protected override Func<BrokeredMessage, Task> Callback => async m =>
        {
            _logger.Info($"{QueueName} queue received a message with correlation id {m.CorrelationId}.");

            if (!m.Properties.ContainsKey(MessageTypeKey))
            {
                await m.DeadLetterAsync(
                    $"The '{MessageTypeKey}' property is missing on the message.",
                    $"The '{MessageTypeKey}' property is required for messages sent to this queue ({QueueName}).");
                _logger.Error($"Discarding a message with correlation id {m.CorrelationId}: '{MessageTypeKey}' missing from property bag.");
                return;
            }

            string content;
            using (var sr = new StreamReader(m.GetBody<Stream>(), Encoding.UTF8))
            {
                try
                {
                    content = await sr.ReadToEndAsync();
                }
                catch (Exception e)
                {
                    await m.DeadLetterAsync(
                        $"An error occurred ({e.GetType().Name}).",
                        $"Failed to process the message due to an error: {e.Message}.");
                    _logger.Error(e, $"Discarding a message with correlation id {m.CorrelationId}: See exception details.");
                    return;
                }
            }

            await TryHandle(m, content);
        };

        private async Task TryHandle(BrokeredMessage m, string content)
        {
            try
            {
                var messageType = (string)m.Properties[MessageTypeKey];
                if (messageType == nameof(FirstRequest))
                {
                    var message = JsonConvert.DeserializeObject<FirstRequest>(content);
                    await _messageHandler.HandleMessageA(message);
                }
                else if (messageType == nameof(SecondRequest))
                {
                    var message = JsonConvert.DeserializeObject<SecondRequest>(content);
                    await _messageHandler.HandleMessageB(message);
                }
                else
                {
                    await m.DeadLetterAsync(
                        $"Messages of type {messageType} are not supported by this queue.",
                        $"Messages of type {messageType} are not supported by this queue ({QueueName}).");
                    _logger.Error($"Discarding a message with correlation id {m.CorrelationId}: Unsupported message type '{messageType}'.");
                }
            }
            catch (Exception e)
            {
                await m.DeadLetterAsync(
                    $"An error occurred ({e.GetType().Name}).",
                    $"Failed to process the message due to an error: {e.Message}.");
                _logger.Error(e, $"Discarding a message with correlation id {m.CorrelationId}: See exception details.");
            }
        }
    }
}