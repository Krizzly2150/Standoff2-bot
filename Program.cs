using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Text.RegularExpressions;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace Standoff2BoostBot
{
    public class Program
    {
        private const string BotToken = "8342644005:AAHTb1NDunMgtJ2dk3VZBpji5xWG9M-JgmI";
        private const string SellerUsername = "krizzly2150";

        // Состояния пользователей
        private static readonly Dictionary<long, UserState> UserStates = new();

        public static async Task Main(string[] args)
        {
            Console.WriteLine("Запуск бота...");

            var host = Host.CreateDefaultBuilder(args)
                .ConfigureServices((context, services) =>
                {
                    services.AddSingleton<ITelegramBotClient>(new TelegramBotClient(BotToken));
                    services.AddHostedService<BotService>();
                })
                .Build();

            await host.RunAsync();
        }

        // Главное меню (под скрепкой)
        private static ReplyKeyboardMarkup GetMainMenuKeyboard()
        {
            return new ReplyKeyboardMarkup(new[]
            {
                new[] { new KeyboardButton("🛒 Купить буст") },
                new[] { new KeyboardButton("👤 Профиль"), new KeyboardButton("⭐ Отзывы") }
            })
            {
                ResizeKeyboard = true,
                OneTimeKeyboard = true
            };
        }

        // Меню выбора режима
        private static InlineKeyboardMarkup GetModeSelectKeyboard()
        {
            var buttons = new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("🎯 Соревновательный [5x5]", "mode_comp") },
                new[] { InlineKeyboardButton.WithCallbackData("🤝 Союзники [2x2]", "mode_allies") },
                new[] { InlineKeyboardButton.WithCallbackData("⚔️ Дуэли [1x1]", "mode_duels") },
                new[] { InlineKeyboardButton.WithCallbackData("↩️ Назад", "back_to_main") }
            };

            return new InlineKeyboardMarkup(buttons);
        }

        // Кнопки выбора способа оплаты
        private static InlineKeyboardMarkup GetPaymentMethodKeyboard()
        {
            var buttons = new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("🔐 Со входом в аккаунт", "payment_account") },
                new[] { InlineKeyboardButton.WithCallbackData("🎮 Через пати (+50% к цене)", "payment_party") },
                new[] { InlineKeyboardButton.WithCallbackData("↩️ Назад", "back_to_modes") }
            };

            return new InlineKeyboardMarkup(buttons);
        }

        // Обработка сообщений
        private static async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            try
            {
                if (update.Type == UpdateType.Message && update.Message != null)
                {
                    await HandleMessage(botClient, update.Message, cancellationToken);
                }
                else if (update.Type == UpdateType.CallbackQuery && update.CallbackQuery != null)
                {
                    await HandleCallbackQuery(botClient, update.CallbackQuery, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка: {ex.Message}");
            }
        }

        private static async Task HandleMessage(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken)
        {
            var chatId = message.Chat.Id;
            var text = message.Text ?? "";

            // Автоматический /start при первом сообщении
            if (!UserStates.ContainsKey(chatId))
            {
                await SendWelcomeMessage(botClient, chatId, cancellationToken);
                UserStates[chatId] = new UserState { CurrentState = UserStateState.MainMenu };
                return;
            }

            var userState = UserStates[chatId];

            // Обработка текстовых команд
            switch (text)
            {
                case "/start":
                    await SendWelcomeMessage(botClient, chatId, cancellationToken);
                    userState.CurrentState = UserStateState.MainMenu;
                    break;

                case "🛒 Купить буст":
                    await ShowPriceList(botClient, chatId, cancellationToken);
                    break;

                case "👤 Профиль":
                    await botClient.SendTextMessageAsync(
                        chatId: chatId,
                        text: "👤 *Ваш профиль*\n\nЗдесь будет информация о вашем профиле и истории заказов.",
                        parseMode: ParseMode.Markdown,
                        replyMarkup: GetMainMenuKeyboard(),
                        cancellationToken: cancellationToken);
                    break;

                case "⭐ Отзывы":
                    await botClient.SendTextMessageAsync(
                        chatId: chatId,
                        text: "⭐ *Отзывы наших клиентов:*\n\n" +
                             "• 'Отличный буст, всё быстро и качественно! 🎯'\n" +
                             "• 'Бустили аккуратно, аккаунт в безопасности 🔐'\n" +
                             "• 'Рекомендую этого бустера! 💯'\n" +
                             "• 'Цены адекватные, работают профессионально ⚡'",
                        parseMode: ParseMode.Markdown,
                        replyMarkup: GetMainMenuKeyboard(),
                        cancellationToken: cancellationToken);
                    break;

                default:
                    // Обработка данных от пользователя
                    if (userState.CurrentState == UserStateState.WaitingForUserData)
                    {
                        await ProcessUserData(botClient, chatId, text, userState, cancellationToken);
                    }
                    else
                    {
                        await botClient.SendTextMessageAsync(
                            chatId: chatId,
                            text: "Пожалуйста, используйте кнопки меню для навигации 📋",
                            replyMarkup: GetMainMenuKeyboard(),
                            cancellationToken: cancellationToken);
                    }
                    break;
            }
        }

        private static async Task SendWelcomeMessage(ITelegramBotClient botClient, long chatId, CancellationToken cancellationToken)
        {
            var welcomeText = "🎮 *Добро пожаловать в буст-сервис Standoff 2!* 🔥\n\n" +
                             "Мы профессионально повысим ваше звание в игре! 🚀\n\n" +
                             "Выберите нужный пункт в меню ниже:";

            await botClient.SendTextMessageAsync(
                chatId: chatId,
                text: welcomeText,
                parseMode: ParseMode.Markdown,
                replyMarkup: GetMainMenuKeyboard(),
                cancellationToken: cancellationToken);
        }

        private static async Task ShowPriceList(ITelegramBotClient botClient, long chatId, CancellationToken cancellationToken)
        {
            var priceText = "─━─━─━─━─━─━─━─🔥 *ПРАЙС* 🔥─━─━─━─━─━─━─━─\n\n" +
                           "• Калибровка 10 игр (400₽/1000🍯)❗\n\n" +
                           "─━─━─━─━─━─━─━─❗*БУСТ С:*❗─━─━─━─━─━─━─━─\n\n" +
                           "• Бронзы 1 до Бронзы 2 (50₽/125🍯);\n" +
                           "• Бронзы 2 до Бронзы 3 (50₽/125🍯);\n" +
                           "• Бронзы 3 до Бронзы 4 (50₽/125🍯);\n" +
                           "• Бронзы 4 до Сильвера 1 (60₽/150🍯);\n\n" +
                           "─━─━─━─━─━─━─━─━─━─━─━─━─━─━─━─━─━─━─\n\n" +
                           "• Сильвера 1 до Сильвера 2 (70₽/175🍯);\n" +
                           "• Сильвера 2 до Сильвера 3 (70₽/175🍯);\n" +
                           "• Сильвера 3 до Сильвера 4 (70₽/175🍯);\n" +
                           "• Сильвера 4 до Голда 1 (80₽/200🍯);\n\n" +
                           "─━─━─━─━─━─━─━─━─━─━─━─━─━─━─━─━─━─━─\n\n" +
                           "• Голда 1 до Голда 2 (90₽/225🍯);\n" +
                           "• Голда 2 до Голда 3 (90₽/225🍯);\n" +
                           "• Голда 3 до Голда 4 (90₽/225🍯);\n" +
                           "• Голда 4 до Феникса (100₽/250🍯);\n\n" +
                           "─━─━─━─━─━─━─━─━─━─━─━─━─━─━─━─━─━─━─\n\n" +
                           "• Феникса до Ренжера (120₽/300🍯);\n" +
                           "• Ренжера до Чемпиона (140₽/350🍯);\n" +
                           "• Чемпиона до Мастера (170₽/425🍯);\n" +
                           "• Мастера до Элиты (200₽/500🍯);\n\n" +
                           "─━─━─━─━─━─━─━─━─━─━─━─━─━─━─━─━─━─━─\n\n" +
                           "• Элиты до Легенды (2500₽/6000🍯).\n\n" +
                           "─━─━─━─━─━─━─━─━─━─━─━─━─━─━─━─━─━─━─\n\n" +
                           "*По поводу покупки буста писать* 👇\n\n" +
                           "─    ─    ─    ─    ─    ─    ─    ─ @kr1zzly2150\n\n" +
                           "─━─━─━─━─━─━─━─━─━─━─━─━─━─━─━─━─━─━─\n\n" +
                           "❗*ЗАПРЕЩЕНО играть в режим, для которого купили буст* (при нарушении деньги не верну, буст не сделаю)❗\n\n" +
                           "─━─━─━─━─━─━─━─━─━─━─━─━─━─━─━─━─━─━─\n\n" +
                           "✔️ *Как купить буст?*\n\n" +
                           "1) Пишите 👉 @kr1zzly2150 :\n" +
                           "• режим (мм / союзники);\n" +
                           "• ваше текущее звание;\n" +
                           "• звание которое хотите получить;\n" +
                           "2) Оплачиваете буст;\n" +
                           "3) Скидываете данные от аккаунта.\n\n" +
                           "─━─━─━─━─━─━─━─━─━─━─━─━─━─━─━─━─━─━─\n\n" +
                           "*Если не хотите скидывать данные от аккаунта, буст через лобби стоит в 2 РАЗА ДОРОЖЕ*❗\n\n" +
                           "─━─━─━─━─━─━─━─━─━─━─━─━─━─━─━─━─━─━─";

            var buttons = new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("🛒 Купить", "buy_now") },
                new[] { InlineKeyboardButton.WithCallbackData("↩️ Назад", "back_to_main") }
            };

            await botClient.SendTextMessageAsync(
                chatId: chatId,
                text: priceText,
                parseMode: ParseMode.Markdown,
                replyMarkup: new InlineKeyboardMarkup(buttons),
                cancellationToken: cancellationToken);
        }

        private static async Task ProcessUserData(ITelegramBotClient botClient, long chatId, string text, UserState userState, CancellationToken cancellationToken)
        {
            // Парсим данные независимо от формата (строчка или столбик)
            var parsedData = ParseUserData(text);

            if (parsedData.Count < 3)
            {
                await botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: "❌ *Пожалуйста, заполните все три пункта:*\n\n" +
                         "1) Ваш текущий MMR\n" +
                         "2) Желаемое звание\n" +
                         "3) Ваш ID\n\n" +
                         "*Пример в строчку:*\n" +
                         "1)1234 2)Легенда 3)51345522\n\n" +
                         "*Пример в столбик:*\n" +
                         "1) 1234\n" +
                         "2) Легенда\n" +
                         "3) 51345522\n\n" +
                         "⚠️ *Главное чтобы были пункты 1) 2) 3)*",
                    parseMode: ParseMode.Markdown,
                    cancellationToken: cancellationToken);
                return;
            }

            // Сохраняем данные в красивом формате для продавца
            userState.UserData = $"1) {parsedData.GetValueOrDefault("1", "Не указано")}\n" +
                                $"2) {parsedData.GetValueOrDefault("2", "Не указано")}\n" +
                                $"3) {parsedData.GetValueOrDefault("3", "Не указано")}";

            userState.CurrentState = UserStateState.ChoosingPaymentMethod;

            await botClient.SendTextMessageAsync(
                chatId: chatId,
                text: "✅ *Данные приняты!*\n\n" +
                     "Как вы желаете приобрести буст?",
                parseMode: ParseMode.Markdown,
                replyMarkup: GetPaymentMethodKeyboard(),
                cancellationToken: cancellationToken);
        }

        // Новый метод для парсинга данных в любом формате
        private static Dictionary<string, string> ParseUserData(string input)
        {
            var result = new Dictionary<string, string>();

            // Убираем лишние пробелы и переносы, но сохраняем структуру
            var cleanedInput = input.Replace("\r", "");

            // Ищем все вхождения пунктов 1), 2), 3) с любыми пробелами
            var regex = new Regex(@"(?<number>[123])\)\s*(?<value>[^\n123]*)", RegexOptions.IgnoreCase);
            var matches = regex.Matches(cleanedInput);

            foreach (Match match in matches)
            {
                if (match.Success)
                {
                    var number = match.Groups["number"].Value;
                    var value = match.Groups["value"].Value.Trim();

                    if (!string.IsNullOrEmpty(value))
                    {
                        result[number] = value;
                    }
                }
            }

            // Если не нашли через regex, пробуем простой split
            if (result.Count < 3)
            {
                var lines = cleanedInput.Split('\n')
                    .Where(line => !string.IsNullOrWhiteSpace(line))
                    .ToArray();

                for (int i = 0; i < Math.Min(lines.Length, 3); i++)
                {
                    var line = lines[i].Trim();
                    // Пытаемся найти номер пункта в начале строки
                    if (line.StartsWith($"{i + 1})") || line.StartsWith($"{i + 1})"))
                    {
                        var value = line.Substring(line.IndexOf(')') + 1).Trim();
                        result[(i + 1).ToString()] = value;
                    }
                    else
                    {
                        // Если нет номера, просто сохраняем по порядку
                        result[(i + 1).ToString()] = line;
                    }
                }
            }

            return result;
        }

        private static async Task HandleCallbackQuery(ITelegramBotClient botClient, CallbackQuery callbackQuery, CancellationToken cancellationToken)
        {
            var data = callbackQuery.Data;
            var chatId = callbackQuery.Message.Chat.Id;
            var messageId = callbackQuery.Message.MessageId;

            try
            {
                switch (data)
                {
                    case "back_to_main":
                        await botClient.DeleteMessageAsync(chatId, messageId, cancellationToken);
                        await SendWelcomeMessage(botClient, chatId, cancellationToken);
                        break;

                    case "buy_now":
                        await botClient.EditMessageTextAsync(
                            chatId: chatId,
                            messageId: messageId,
                            text: "🎮 *Выберите режим игры:*",
                            parseMode: ParseMode.Markdown,
                            replyMarkup: GetModeSelectKeyboard(),
                            cancellationToken: cancellationToken);
                        break;

                    case "mode_comp":
                    case "mode_allies":
                    case "mode_duels":
                        var modeName = data switch
                        {
                            "mode_comp" => "Соревновательный [5x5]",
                            "mode_allies" => "Союзники [2x2]",
                            "mode_duels" => "Дуэли [1x1]",
                            _ => "Режим"
                        };

                        if (!UserStates.ContainsKey(chatId))
                            UserStates[chatId] = new UserState();

                        UserStates[chatId].SelectedMode = modeName;
                        UserStates[chatId].CurrentState = UserStateState.WaitingForUserData;

                        await botClient.EditMessageTextAsync(
                            chatId: chatId,
                            messageId: messageId,
                            text: $"🎯 *Выбран режим: {modeName}*\n\n" +
                                 "*Пожалуйста, напишите:*\n" +
                                 "1) Ваш текущий MMR\n" +
                                 "2) Желаемое звание\n" +
                                 "3) Ваш ID\n\n" +
                                 "*Пример в строчку:*\n" +
                                 "1)1234 2)Легенда 3)51345522\n\n" +
                                 "*Пример в столбик:*\n" +
                                 "1) 1234\n" +
                                 "2) Легенда\n" +
                                 "3) 51345522\n\n" +
                                 "⚠️ *Главное чтобы были пункты 1) 2) 3)*",
                            parseMode: ParseMode.Markdown,
                            cancellationToken: cancellationToken);
                        break;

                    case "back_to_modes":
                        await botClient.EditMessageTextAsync(
                            chatId: chatId,
                            messageId: messageId,
                            text: "🎮 *Выберите режим игры:*",
                            parseMode: ParseMode.Markdown,
                            replyMarkup: GetModeSelectKeyboard(),
                            cancellationToken: cancellationToken);
                        break;

                    case "payment_account":
                    case "payment_party":
                        await ProcessPaymentMethod(botClient, chatId, data, cancellationToken);
                        break;

                    default:
                        await botClient.AnswerCallbackQueryAsync(callbackQuery.Id, "Неизвестная команда", cancellationToken: cancellationToken);
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка: {ex.Message}");
            }

            await botClient.AnswerCallbackQueryAsync(callbackQuery.Id, cancellationToken: cancellationToken);
        }

        private static async Task ProcessPaymentMethod(ITelegramBotClient botClient, long chatId, string paymentMethod, CancellationToken cancellationToken)
        {
            if (!UserStates.ContainsKey(chatId) || string.IsNullOrEmpty(UserStates[chatId].UserData))
            {
                await botClient.SendTextMessageAsync(chatId, "❌ Ошибка данных", cancellationToken: cancellationToken);
                return;
            }

            var userState = UserStates[chatId];
            var paymentType = paymentMethod == "payment_account" ? "со входом в аккаунт" : "через пати (+50% к цене)";

            // Формируем красивый текст для продавца
            var orderText = $"🛒 *НОВЫЙ ЗАКАЗ* 🛒\n\n" +
                           $"👤 *Покупатель:* {(userState.UserName != null ? "@" + userState.UserName : "Unknown")}\n" +
                           $"🆔 *ID покупателя:* {chatId}\n" +
                           $"🎮 *Режим:* {userState.SelectedMode}\n" +
                           $"💳 *Способ:* {paymentType}\n\n" +
                           $"─━─━─━─━─━─━─━─━─━─━─━─━─━─\n\n" +
                           $"📋 *Данные покупателя:*\n{userState.UserData}\n\n" +
                           $"─━─━─━─━─━─━─━─━─━─━─━─━─━─\n\n" +
                           $"⏰ *Время:* {DateTime.Now:dd.MM.yyyy HH:mm}\n" +
                           $"🔗 *Ссылка:* [Написать покупателю](tg://user?id={chatId})";

            try
            {
                // Логируем заказ в консоль
                Console.WriteLine("=== ЗАКАЗ ДЛЯ ПРОДАВЦА ===");
                Console.WriteLine(orderText);
                Console.WriteLine("=========================");

                // Сохраняем в файл для продавца (с указанием полного пространства имен)
                System.IO.File.AppendAllText("orders_for_seller.log", $"\n\n{orderText}\n");

                // Здесь будет код отправки продавцу @krizzly2150
                // await botClient.SendTextMessageAsync(
                //     chatId: SELLER_CHAT_ID, // chat_id продавца
                //     text: orderText,
                //     parseMode: ParseMode.Markdown,
                //     cancellationToken: cancellationToken);

            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка отправки заказа продавцу: {ex.Message}");
                System.IO.File.AppendAllText("error_orders.log", $"\nОшибка: {ex.Message}\nЗаказ: {orderText}\n");
            }

            // Сообщение для ПОКУПАТЕЛЯ (без информации о заказе)
            var userMessage = $"✅ *Заказ оформлен!* 🎉\n\n" +
                             $"📞 *Свяжитесь с продавцом:* [@{SellerUsername}](https://t.me/{SellerUsername})\n\n" +
                             $"⚡ *Продавец свяжется с вами в ближайшее время для уточнения деталей!*\n\n" +
                             $"💡 *Не забудьте ответить продавцу когда он напишет!*";

            // Кнопка для быстрого перехода к продавцу
            var sellerKeyboard = new InlineKeyboardMarkup(
                InlineKeyboardButton.WithUrl("💬 Написать продавцу", $"https://t.me/{SellerUsername}"));

            await botClient.SendTextMessageAsync(
                chatId: chatId,
                text: userMessage,
                parseMode: ParseMode.Markdown,
                replyMarkup: sellerKeyboard,
                cancellationToken: cancellationToken);

            // Сбрасываем состояние пользователя
            userState.CurrentState = UserStateState.MainMenu;
            userState.UserData = null;
            userState.SelectedMode = null;

            // Сохраняем информацию о заказе для истории
            SaveOrderToHistory(chatId, userState.UserName ?? "Unknown", orderText);
        }

        // Добавляем недостающий метод SaveOrderToHistory
        private static void SaveOrderToHistory(long userId, string userName, string orderText)
        {
            try
            {
                var historyEntry = $"\n\n=== ЗАКАЗ {DateTime.Now:dd.MM.yyyy HH:mm} ===\n" +
                                  $"UserID: {userId}\n" +
                                  $"Username: {userName}\n" +
                                  orderText +
                                  "\n=====================\n";

                System.IO.File.AppendAllText("orders_history.log", historyEntry);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка сохранения истории: {ex.Message}");
            }
        }

        private static Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
        {
            var errorMessage = exception switch
            {
                ApiRequestException apiRequestException
                    => $"Telegram API Error: [{apiRequestException.ErrorCode}] {apiRequestException.Message}",
                _ => exception.ToString()
            };

            Console.WriteLine($"Ошибка: {errorMessage}");
            return Task.CompletedTask;
        }

        public class BotService : IHostedService
        {
            private readonly ITelegramBotClient _botClient;
            private CancellationTokenSource? _cts;

            public BotService(ITelegramBotClient botClient)
            {
                _botClient = botClient;
            }

            public async Task StartAsync(CancellationToken cancellationToken)
            {
                _cts = new CancellationTokenSource();

                var receiverOptions = new ReceiverOptions
                {
                    AllowedUpdates = Array.Empty<UpdateType>()
                };

                _botClient.StartReceiving(
                    updateHandler: HandleUpdateAsync,
                    pollingErrorHandler: HandlePollingErrorAsync,
                    receiverOptions: receiverOptions,
                    cancellationToken: _cts.Token
                );

                var me = await _botClient.GetMeAsync(cancellationToken);
                Console.WriteLine($"🤖 Бот запущен: @{me.Username}");
                Console.WriteLine("📍 Напишите боту в Telegram для начала работы!");
            }

            public Task StopAsync(CancellationToken cancellationToken)
            {
                _cts?.Cancel();
                return Task.CompletedTask;
            }
        }

        public class UserState
        {
            public UserStateState CurrentState { get; set; } = UserStateState.MainMenu;
            public string? UserData { get; set; }
            public string? SelectedMode { get; set; }
            public string? UserName { get; set; }
        }

        public enum UserStateState
        {
            MainMenu,
            WaitingForUserData,
            ChoosingPaymentMethod
        }
    }
}