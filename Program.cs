using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Text.RegularExpressions;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using System.Text.Json;
using System.IO;

namespace Standoff2BoostBot
{
    public class Program
    {
        private static string BotToken => Environment.GetEnvironmentVariable("BOT_TOKEN") ?? "8342644005:AAHTb1NDunMgtJ2dk3VZBpji5xWG9M-JgmI";
        private static string SellerUsername => Environment.GetEnvironmentVariable("SELLER_USERNAME") ?? "krizzly2150";
        private static string OrdersGroupId => Environment.GetEnvironmentVariable("ORDERS_GROUP_ID") ?? "-1002946352030";
        private static string ArchiveGroupId => Environment.GetEnvironmentVariable("ARCHIVE_GROUP_ID") ?? "-1002675852102";
        private static string ReviewsGroupId => Environment.GetEnvironmentVariable("REVIEWS_GROUP_ID") ?? "@krizzlyreviews";

        private static readonly Dictionary<long, UserState> UserStates = new();
        private static readonly Dictionary<long, DateTime> LastActiveMap = new();
        private static int orderCounter = 1;

        public static async Task Main(string[] args)
        {
            LoadOrderCounter();
            LoadUserData();

            while (true)
            {
                try
                {
                    Console.WriteLine($"🔄 Запуск бота {DateTime.Now}");

                    using var host = Host.CreateDefaultBuilder(args)
                        .ConfigureServices((context, services) =>
                        {
                            services.AddSingleton<ITelegramBotClient>(new TelegramBotClient(BotToken));
                            services.AddHostedService<BotService>();
                        })
                        .Build();

                    await host.RunAsync();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"💥 Ошибка: {ex.Message}");
                    Console.WriteLine("⏳ Перезапуск через 30 секунд...");
                    await Task.Delay(30000);
                }
            }
        }

        private static void LoadOrderCounter()
        {
            if (System.IO.File.Exists("order_counter.json"))
            {
                var data = System.IO.File.ReadAllText("order_counter.json");
                orderCounter = JsonSerializer.Deserialize<int>(data);
            }
        }

        private static void SaveOrderCounter()
        {
            var data = JsonSerializer.Serialize(orderCounter);
            System.IO.File.WriteAllText("order_counter.json", data);
        }

        private static void LoadUserData()
        {
            if (System.IO.File.Exists("users.json"))
            {
                var data = System.IO.File.ReadAllText("users.json");
                var users = JsonSerializer.Deserialize<List<UserData>>(data);
                if (users != null)
                {
                    foreach (var user in users)
                    {
                        LastActiveMap[user.UserId] = user.LastActive;
                    }
                }
            }
        }

        private static void SaveUserToDatabase(long userId, string? username)
        {
            var userData = new UserData
            {
                UserId = userId,
                Username = username,
                JoinDate = DateTime.Now,
                LastActive = DateTime.Now,
                CompletedOrders = new List<CompletedOrder>()
            };

            var users = new List<UserData>();
            if (System.IO.File.Exists("users.json"))
            {
                var data = System.IO.File.ReadAllText("users.json");
                users = JsonSerializer.Deserialize<List<UserData>>(data) ?? new List<UserData>();
            }

            var existingUser = users.FirstOrDefault(u => u.UserId == userId);
            if (existingUser == null)
            {
                users.Add(userData);
            }
            else
            {
                existingUser.LastActive = DateTime.Now;
            }

            var json = JsonSerializer.Serialize(users);
            System.IO.File.WriteAllText("users.json", json);
        }

        private static ReplyKeyboardMarkup GetMainMenuKeyboard()
        {
            return new ReplyKeyboardMarkup(new[]
            {
                new[] { new KeyboardButton("🛒 Купить буст") },
                new[] { new KeyboardButton("👤 Профиль"), new KeyboardButton("⭐ Отзывы") }
            })
            {
                ResizeKeyboard = true
            };
        }

        private static async Task SendWelcomeMessage(ITelegramBotClient botClient, long chatId, CancellationToken cancellationToken)
        {
            var welcomeText = @"🎮 *Добро пожаловать в буст-сервис Standoff 2!* 🔥

Мы профессионально повысим ваше звание в игре! 🚀

Выберите нужный пункт в меню ниже:";

            await botClient.SendTextMessageAsync(
                chatId: chatId,
                text: welcomeText,
                parseMode: ParseMode.Markdown,
                replyMarkup: GetMainMenuKeyboard(),
                cancellationToken: cancellationToken);
        }

        private static async Task ShowPriceList(ITelegramBotClient botClient, long chatId, CancellationToken cancellationToken)
        {
            var priceText = @"🎮 *ПРАЙС-ЛИСТ БУСТА STANDOFF 2* 🎮

💰 *Цены за 1 звание:*

• *Калибровка:* 100₽ / 250🍯 (1 игра)
• *Бронза → Серебро:* 50₽ / 125🍯
• *Серебро → Золото:* 75₽ / 188🍯  
• *Золото → Феникс:* 100₽ / 250🍯
• *Феникс → Ренжер:* 120₽ / 300🍯
• *Ренжер → Чемпион:* 140₽ / 350🍯
• *Чемпион → Мастер:* 160₽ / 400🍯
• *Мастер → Элита:* 200₽ / 500🍯
• *Элита → Легенда:* 999₽ / 2500🍯

⚠️ *ВАЖНАЯ ИНФОРМАЦИЯ:*
┌─────────────────────────────
│ *Без входа в аккаунт* (+50% к цене)
│ *ТОЛЬКО в режиме Союзники*
└─────────────────────────────

💡 *Бот автоматически рассчитает итоговую стоимость!*";

            // Убираем кнопку "Назад", оставляем только "Купить"
            var buttons = new[]
            {
        new[] { InlineKeyboardButton.WithCallbackData("🛒 Купить буст", "buy_now") }
    };

            await botClient.SendTextMessageAsync(
                chatId: chatId,
                text: priceText,
                parseMode: ParseMode.Markdown,
                replyMarkup: new InlineKeyboardMarkup(buttons),
                cancellationToken: cancellationToken);
        }

        private static int CalculatePrice(string? currentRank, string? targetRank, bool isLobbyBoost)
        {
            if (string.IsNullOrEmpty(currentRank) || string.IsNullOrEmpty(targetRank))
            {
                return 0;
            }

            var rankValues = new Dictionary<string, int>
    {
        {"бронза 1", 1}, {"бронза 2", 2}, {"бронза 3", 3}, {"бронза 4", 4},
        {"серебро 1", 5}, {"серебро 2", 6}, {"серебро 3", 7}, {"серебро 4", 8},
        {"золото 1", 9}, {"золото 2", 10}, {"золото 3", 11}, {"золото 4", 12},
        {"феникс", 13}, {"ренжер", 14}, {"чемпион", 15}, {"мастер", 16}, {"элита", 17}, {"легенда", 18}
    };

            var pricePerRank = new Dictionary<int, int>
    {
        {1, 50}, {2, 50}, {3, 50}, {4, 60},
        {5, 70}, {6, 70}, {7, 70}, {8, 80},
        {9, 90}, {10, 90}, {11, 90}, {12, 100},
        {13, 120}, {14, 140}, {15, 160}, {16, 200}, {17, 999}
    };

            int currentValue = rankValues[currentRank.ToLower()];
            int targetValue = rankValues[targetRank.ToLower()];
            int totalPrice = 0;

            for (int i = currentValue; i < targetValue; i++)
            {
                totalPrice += pricePerRank[i];
            }

            if (isLobbyBoost)
            {
                totalPrice = (int)(totalPrice * 1.5);
            }

            return totalPrice;
        }

        private static async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            try
            {
                // Обработка callback от кнопок
                if (update.CallbackQuery is { } callbackQuery)
                {
                    await HandleCallbackQuery(botClient, callbackQuery, cancellationToken);
                    return;
                }

                // Обработка сообщений
                if (update.Message is { } message)
                {
                    await HandleMessage(botClient, message, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }

        private static async Task HandleMessage(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken)
        {
            var chatId = message.Chat.Id;
            var text = message.Text ?? "";

            if (!UserStates.ContainsKey(chatId) || (LastActiveMap.ContainsKey(chatId) && (DateTime.Now - LastActiveMap[chatId]).TotalHours >= 24))
            {
                // СОХРАНЯЕМ username из сообщения
                var username = message.From?.Username ?? "Не указан";
                SaveUserToDatabase(chatId, username);

                await botClient.SendTextMessageAsync(chatId, "/start", cancellationToken: cancellationToken);
                UserStates[chatId] = new UserState
                {
                    CurrentState = UserStateState.MainMenu,
                    UserName = username // Сохраняем в состоянии
                };
                LastActiveMap[chatId] = DateTime.Now;
                return;
            }

            LastActiveMap[chatId] = DateTime.Now;
            var userState = UserStates[chatId];

            switch (text)
            {
                case "/start":
                    await SendWelcomeMessage(botClient, chatId, cancellationToken);
                    userState.CurrentState = UserStateState.MainMenu;
                    break;

                case "🛒 Купить буст":
                    // Прямой переход к прайсу без приветствия
                    await ShowPriceList(botClient, chatId, cancellationToken);
                    break;

                case "👤 Профиль":
                    await ShowUserProfile(botClient, chatId, cancellationToken);
                    break;

                case "⭐ Отзывы":
                    // Для публичной группы используем username
                    string reviewsUrl;

                    if (ReviewsGroupId.StartsWith("@"))
                    {
                        // Если указан username типа @groupname
                        reviewsUrl = $"https://t.me/{ReviewsGroupId.Substring(1)}";
                    }
                    else if (ReviewsGroupId.StartsWith("https://"))
                    {
                        // Если уже полная ссылка
                        reviewsUrl = ReviewsGroupId;
                    }
                    else
                    {
                        // Если числовой ID (на всякий случай)
                        reviewsUrl = $"https://t.me/c/{ReviewsGroupId.Substring(4)}";
                    }

                    var reviewsKeyboard = new InlineKeyboardMarkup(
                        InlineKeyboardButton.WithUrl("📝 Читать все отзывы", reviewsUrl)
                    );

                    await botClient.SendTextMessageAsync(
                        chatId: chatId,
                        text: "⭐ *Отзывы наших клиентов*\n\nНажмите кнопку ниже чтобы посмотреть реальные отзывы покупателей и оставить свой отзыв:",
                        parseMode: ParseMode.Markdown,
                        replyMarkup: reviewsKeyboard,
                        cancellationToken: cancellationToken);
                    break;

                default:
                    if (userState.CurrentState == UserStateState.WaitingForPlayerID)
                    {
                        await ProcessPlayerIDInput(botClient, chatId, text, userState, cancellationToken);
                    }
                    else
                    {
                        // Если пользователь в главном меню и пишет что-то другое - показываем меню
                        await botClient.SendTextMessageAsync(
                            chatId: chatId,
                            text: "Пожалуйста, используйте кнопки меню для навигации 📋",
                            replyMarkup: GetMainMenuKeyboard(),
                            cancellationToken: cancellationToken);
                    }
                    break;
            }
        }

        private static async Task ShowUserProfile(ITelegramBotClient botClient, long chatId, CancellationToken cancellationToken)
        {
            var users = new List<UserData>();
            if (System.IO.File.Exists("users.json"))
            {
                var data = System.IO.File.ReadAllText("users.json");
                users = JsonSerializer.Deserialize<List<UserData>>(data) ?? new List<UserData>();
            }

            var user = users.FirstOrDefault(u => u.UserId == chatId);
            var joinTime = user != null ? (DateTime.Now - user.JoinDate) : TimeSpan.Zero;

            var profileText = $@"👤 *Ваш профиль*

📅 *Участник с:* {user?.JoinDate:dd.MM.yyyy}
⏰ *В сообществе:* {joinTime.Days} дней {joinTime.Hours} часов
🎯 *Завершенных бустов:* {user?.CompletedOrders?.Count ?? 0}

💎 *Статус:* {(user?.CompletedOrders?.Count >= 5 ? "🥇 Постоянный клиент" : user?.CompletedOrders?.Count >= 3 ? "🥈 Активный клиент" : "🥉 Новый клиент")}";

            // Создаем клавиатуру с кнопкой истории заказов
            var profileKeyboard = new InlineKeyboardMarkup(new[]
            {
        new[]
        {
            InlineKeyboardButton.WithCallbackData("📋 История заказов", $"show_history_{chatId}")
        }
    });

            await botClient.SendTextMessageAsync(
                chatId: chatId,
                text: profileText,
                parseMode: ParseMode.Markdown,
                replyMarkup: profileKeyboard,
                cancellationToken: cancellationToken);
        }

        private static async Task ShowOrdersHistory(ITelegramBotClient botClient, CallbackQuery callbackQuery, long userId, CancellationToken cancellationToken)
        {
            var users = new List<UserData>();
            if (System.IO.File.Exists("users.json"))
            {
                var data = System.IO.File.ReadAllText("users.json");
                users = JsonSerializer.Deserialize<List<UserData>>(data) ?? new List<UserData>();
            }

            var user = users.FirstOrDefault(u => u.UserId == userId);

            if (user?.CompletedOrders?.Count > 0)
            {
                var ordersList = user.CompletedOrders
                    .OrderByDescending(o => o.CompletionDate)
                    .Select(o => $"• #{o.OrderNumber}: {o.FromRank} → {o.ToRank} ({o.Price}₽) - {o.CompletionDate:dd.MM.yy}")
                    .ToArray();

                var historyText = $@"📋 *История заказов* ({user.CompletedOrders.Count})

{string.Join("\n", ordersList)}";

                await botClient.EditMessageTextAsync(
                    chatId: callbackQuery.Message.Chat.Id,
                    messageId: callbackQuery.Message.MessageId,
                    text: historyText,
                    parseMode: ParseMode.Markdown,
                    replyMarkup: new InlineKeyboardMarkup(new[]
                    {
                new[] { InlineKeyboardButton.WithCallbackData("⬆️ Свернуть", $"collapse_history_{userId}") }
                    }),
                    cancellationToken: cancellationToken);
            }
            else
            {
                await botClient.EditMessageTextAsync(
                    chatId: callbackQuery.Message.Chat.Id,
                    messageId: callbackQuery.Message.MessageId,
                    text: "📋 *История заказов*\n\nПока нет завершенных заказов",
                    parseMode: ParseMode.Markdown,
                    replyMarkup: new InlineKeyboardMarkup(new[]
                    {
                new[] { InlineKeyboardButton.WithCallbackData("⬆️ Свернуть", $"collapse_history_{userId}") }
                    }),
                    cancellationToken: cancellationToken);
            }
        }

        private static async Task CollapseOrdersHistory(ITelegramBotClient botClient, CallbackQuery callbackQuery, long userId, CancellationToken cancellationToken)
        {
            var users = new List<UserData>();
            if (System.IO.File.Exists("users.json"))
            {
                var data = System.IO.File.ReadAllText("users.json");
                users = JsonSerializer.Deserialize<List<UserData>>(data) ?? new List<UserData>();
            }

            var user = users.FirstOrDefault(u => u.UserId == userId);
            var joinTime = user != null ? (DateTime.Now - user.JoinDate) : TimeSpan.Zero;

            var profileText = $@"👤 *Ваш профиль*

📅 *Участник с:* {user?.JoinDate:dd.MM.yyyy}
⏰ *В сообществе:* {joinTime.Days} дней {joinTime.Hours} часов
🎯 *Завершенных бустов:* {user?.CompletedOrders?.Count ?? 0}

💎 *Статус:* {(user?.CompletedOrders?.Count >= 5 ? "🥇 Постоянный клиент" : user?.CompletedOrders?.Count >= 3 ? "🥈 Активный клиент" : "🥉 Новый клиент")}";

            await botClient.EditMessageTextAsync(
                chatId: callbackQuery.Message.Chat.Id,
                messageId: callbackQuery.Message.MessageId,
                text: profileText,
                parseMode: ParseMode.Markdown,
                replyMarkup: new InlineKeyboardMarkup(new[]
                {
            new[] { InlineKeyboardButton.WithCallbackData("📋 История заказов", $"show_history_{userId}") }
                }),
                cancellationToken: cancellationToken);
        }

        private static async Task ProcessPlayerIDInput(ITelegramBotClient botClient, long chatId, string text, UserState userState, CancellationToken cancellationToken)
        {
            if (!long.TryParse(text, out long playerId) || text.Length < 8 || text.Length > 9)
            {
                await botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: "❌ *Неверный формат ID!*\n\nID должен содержать 8-9 цифр\n📝 Пример: 51345522",
                    parseMode: ParseMode.Markdown,
                    cancellationToken: cancellationToken);
                return;
            }

            userState.PlayerID = playerId;
            userState.CurrentState = UserStateState.Confirmation;

            var totalPrice = CalculatePrice(userState.CurrentRank!, userState.TargetRank!, userState.IsLobbyBoost);
            var honey = (int)(totalPrice * 2.5);

            var confirmationText = $@"📋 *Проверьте данные заказа:*

🎮 *Режим:* {userState.SelectedMode}
{(userState.IsLobbyBoost ? "💳 *Тип:* Через лобби (+50%)\n" : "💳 *Тип:* Со входом в аккаунт\n")}
📊 *Текущее звание:* {userState.CurrentRank}
⭐ *Целевое звание:* {userState.TargetRank}
🆔 *ID аккаунта:* `{userState.PlayerID}`

💵 *Итоговая стоимость:* {totalPrice}₽ / {honey}🍯";

            await botClient.SendTextMessageAsync(
                chatId: chatId,
                text: confirmationText,
                parseMode: ParseMode.Markdown,
                replyMarkup: new InlineKeyboardMarkup(new[]
                {
                    new[]
                    {
                        InlineKeyboardButton.WithCallbackData("✅ Подтвердить", "confirm_order"),
                        InlineKeyboardButton.WithCallbackData("❌ Отменить", "cancel_order")
                    }
                }),
                cancellationToken: cancellationToken);
        }

        // Добавьте эти методы в класс Program

        private static InlineKeyboardMarkup GetBoostTypeKeyboard()
        {
            return new InlineKeyboardMarkup(new[]
            {
        new[] { InlineKeyboardButton.WithCallbackData("🔐 Со входом в аккаунт", "boost_account") },
        new[] { InlineKeyboardButton.WithCallbackData("🎮 Через лобби (+50%)", "boost_lobby") },
        new[] { InlineKeyboardButton.WithCallbackData("↩️ Назад в меню", "back_to_main") }
    });
        }

        private static InlineKeyboardMarkup GetRanksKeyboard(string rankType, string? currentRank = null)
        {
            var allRanks = new[] {
        "Бронза 1", "Бронза 2", "Бронза 3", "Бронза 4",
        "Серебро 1", "Серебро 2", "Серебро 3", "Серебро 4",
        "Золото 1", "Золото 2", "Золото 3", "Золото 4",
        "Феникс", "Ренжер", "Чемпион", "Мастер", "Элита", "Легенда"
    };

            string[] ranks;

            if (rankType == "target" && !string.IsNullOrEmpty(currentRank))
            {
                // Фильтруем только звания выше текущего
                var rankValues = new Dictionary<string, int>
        {
            {"бронза 1", 1}, {"бронза 2", 2}, {"бронза 3", 3}, {"бронза 4", 4},
            {"серебро 1", 5}, {"серебро 2", 6}, {"серебро 3", 7}, {"серебро 4", 8},
            {"золото 1", 9}, {"золото 2", 10}, {"золото 3", 11}, {"золото 4", 12},
            {"феникс", 13}, {"ренжер", 14}, {"чемпион", 15}, {"мастер", 16}, {"элита", 17}, {"легенда", 18}
        };

                int currentValue = rankValues[currentRank.ToLower()];
                ranks = allRanks.Where(r => rankValues[r.ToLower()] > currentValue).ToArray();
            }
            else
            {
                ranks = rankType switch
                {
                    "current" => allRanks.Take(17).ToArray(), // Все кроме Легенды
                    "target" => allRanks.Skip(1).ToArray(),   // Все кроме Бронзы 1
                    _ => allRanks
                };
            }

            var buttons = new List<InlineKeyboardButton[]>();
            for (int i = 0; i < ranks.Length; i += 2)
            {
                var row = new List<InlineKeyboardButton>();
                row.Add(InlineKeyboardButton.WithCallbackData(ranks[i], $"rank_{ranks[i].ToLower().Replace(" ", "_")}"));
                if (i + 1 < ranks.Length)
                {
                    row.Add(InlineKeyboardButton.WithCallbackData(ranks[i + 1], $"rank_{ranks[i + 1].ToLower().Replace(" ", "_")}"));
                }
                buttons.Add(row.ToArray());
            }

            return new InlineKeyboardMarkup(buttons);
        }

        private static async Task HandleCallbackQuery(ITelegramBotClient botClient, CallbackQuery callbackQuery, CancellationToken cancellationToken)
        {   
            var data = callbackQuery.Data;
            var chatId = callbackQuery.Message.Chat.Id;
            var messageId = callbackQuery.Message.MessageId;

            try
            {
                // Обработка заказов (только для группы Orders)
                if (data.StartsWith("accept_order_") || data.StartsWith("delete_order_") ||
                    data.StartsWith("complete_order_") || data.StartsWith("cancel_accepted_"))
                {
                    await HandleOrderActions(botClient, callbackQuery, cancellationToken);
                    return;
                }


                // В метод HandleCallbackQuery добавляем:
                else if (data.StartsWith("show_history_"))
                {
                    var userId = long.Parse(data.Replace("show_history_", ""));
                    await ShowOrdersHistory(botClient, callbackQuery, userId, cancellationToken);
                }
                else if (data.StartsWith("collapse_history_"))
                {
                    var userId = long.Parse(data.Replace("collapse_history_", ""));
                    await CollapseOrdersHistory(botClient, callbackQuery, userId, cancellationToken);
                }

                // В метод HandleCallbackQuery добавляем:
                else if (data.StartsWith("details_"))
                {
                    var orderNumber = int.Parse(data.Replace("details_", ""));
                    await ShowOrderDetails(botClient, callbackQuery, orderNumber, cancellationToken);
                }
                else if (data.StartsWith("collapse_"))
                {
                    var orderNumber = int.Parse(data.Replace("collapse_", ""));
                    await CollapseOrderDetails(botClient, callbackQuery, orderNumber, cancellationToken);
                }
                // Остальная логика для пользовательских чатов
                if (data == "buy_now")
                {
                    await botClient.EditMessageTextAsync(
                        chatId: chatId,
                        messageId: messageId,
                        text: "🎮 *Выберите тип буста:*",
                        parseMode: ParseMode.Markdown,
                        replyMarkup: GetBoostTypeKeyboard(),
                        cancellationToken: cancellationToken);
                }
                else if (data.StartsWith("boost_"))
                {
                    var boostType = data.Replace("boost_", "");
                    if (!UserStates.ContainsKey(chatId))
                    {
                        UserStates[chatId] = new UserState();
                    }

                    var userState = UserStates[chatId];
                    userState.IsLobbyBoost = boostType == "lobby";
                    userState.CurrentState = UserStateState.ChoosingCurrentRank;

                    await botClient.EditMessageTextAsync(
                        chatId: chatId,
                        messageId: messageId,
                        text: "📊 *Выберите ваше текущее звание:*",
                        parseMode: ParseMode.Markdown,
                        replyMarkup: GetRanksKeyboard("current"),
                        cancellationToken: cancellationToken);
                }
                else if (data.StartsWith("rank_"))
                {
                    var rank = data.Replace("rank_", "").Replace("_", " ");
                    if (!UserStates.ContainsKey(chatId))
                    {
                        UserStates[chatId] = new UserState();
                    }

                    var userState = UserStates[chatId];

                    if (userState.CurrentState == UserStateState.ChoosingCurrentRank)
                    {
                        userState.CurrentRank = rank;
                        userState.CurrentState = UserStateState.ChoosingTargetRank;

                        await botClient.EditMessageTextAsync(
                            chatId: chatId,
                            messageId: messageId,
                            text: $"📊 *Текущее звание: {rank}*\n\n⭐ *Выберите желаемое звание:*",
                            parseMode: ParseMode.Markdown,
                            replyMarkup: GetRanksKeyboard("target", rank),
                            cancellationToken: cancellationToken);
                    }
                    else if (userState.CurrentState == UserStateState.ChoosingTargetRank)
                    {
                        userState.TargetRank = rank;

                        if (!userState.IsLobbyBoost)
                        {
                            userState.CurrentState = UserStateState.Confirmation;
                            await ShowOrderConfirmation(botClient, chatId, userState, cancellationToken);
                        }
                        else
                        {
                            userState.CurrentState = UserStateState.WaitingForPlayerID;
                            await botClient.EditMessageTextAsync(
                                chatId: chatId,
                                messageId: messageId,
                                text: $"✅ *Выбрано звание: {rank}*\n\n🆔 *Введите ваш игровой ID (8-9 цифр):*\n📝 Пример: 51345522",
                                parseMode: ParseMode.Markdown,
                                cancellationToken: cancellationToken);
                        }
                    }
                }
                else if (data == "confirm_order")
                {
                    await ProcessOrderConfirmation(botClient, chatId, cancellationToken);
                }
                else if (data == "cancel_order")
                {
                    if (UserStates.ContainsKey(chatId))
                    {
                        UserStates[chatId].CurrentState = UserStateState.MainMenu;
                    }

                    await botClient.EditMessageTextAsync(
                        chatId: chatId,
                        messageId: messageId,
                        text: "❌ *Заказ отменен*",
                        parseMode: ParseMode.Markdown,
                        cancellationToken: cancellationToken);
                }
                else if (data == "back_to_main")
                {
                    await botClient.DeleteMessageAsync(chatId, messageId, cancellationToken);
                    await SendWelcomeMessage(botClient, chatId, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка: {ex.Message}");
            }

            await botClient.AnswerCallbackQueryAsync(callbackQuery.Id, cancellationToken: cancellationToken);
        }

        // Отдельный метод для обработки действий с заказами
        private static async Task HandleOrderActions(ITelegramBotClient botClient, CallbackQuery callbackQuery, CancellationToken cancellationToken)
        {
            var data = callbackQuery.Data;
            var chatId = callbackQuery.Message.Chat.Id;

            // Преобразуем string ID группы в long для сравнения
            long ordersGroupIdLong = long.Parse(OrdersGroupId);

            // Проверяем, что действие происходит в группе Orders
            if (chatId != ordersGroupIdLong)
            {
                await botClient.AnswerCallbackQueryAsync(
                    callbackQuery.Id,
                    "❌ Действие доступно только в группе заказов",
                    cancellationToken: cancellationToken
                );
                return;
            }

            try
            {
                if (data.StartsWith("accept_order_"))
                {
                    var orderNumber = int.Parse(data.Replace("accept_order_", ""));
                    await AcceptOrder(botClient, callbackQuery, orderNumber, cancellationToken);
                }
                else if (data.StartsWith("delete_order_"))
                {
                    var orderNumber = int.Parse(data.Replace("delete_order_", ""));
                    await DeleteOrder(botClient, callbackQuery, orderNumber, cancellationToken);
                }
                else if (data.StartsWith("complete_order_"))
                {
                    var orderNumber = int.Parse(data.Replace("complete_order_", ""));
                    await CompleteOrder(botClient, callbackQuery, orderNumber, cancellationToken);
                }
                else if (data.StartsWith("cancel_accepted_"))
                {
                    var orderNumber = int.Parse(data.Replace("cancel_accepted_", ""));
                    await CancelAcceptedOrder(botClient, callbackQuery, orderNumber, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка обработки заказа: {ex.Message}");
                await botClient.AnswerCallbackQueryAsync(
                    callbackQuery.Id,
                    "❌ Ошибка обработки заказа",
                    cancellationToken: cancellationToken
                );
            }
        }

        private static async Task CancelAcceptedOrder(ITelegramBotClient botClient, CallbackQuery callbackQuery, int orderNumber, CancellationToken cancellationToken)
        {
            var messageId = callbackQuery.Message.MessageId;
            var chatId = callbackQuery.Message.Chat.Id;

            // Преобразуем string ID группы в long для сравнения
            long ordersGroupIdLong = long.Parse(OrdersGroupId);

            // Проверяем, что действие происходит в группе Orders
            if (chatId != ordersGroupIdLong)
            {
                await botClient.AnswerCallbackQueryAsync(
                    callbackQuery.Id,
                    "❌ Действие доступно только в группе заказов",
                    cancellationToken: cancellationToken
                );
                return;
            }

            // Загружаем информацию о заказе из базы
            var order = LoadOrderFromDatabase(orderNumber);
            if (order == null)
            {
                await botClient.AnswerCallbackQueryAsync(callbackQuery.Id, "❌ Заказ не найден", cancellationToken: cancellationToken);
                return;
            }

            // Отправляем короткое уведомление в группу Order
            var notificationText = $@"🗑️ *Заказ #{orderNumber} отменен*
❌ *Кто:* @{callbackQuery.From.Username}
⏰ *Когда:* {DateTime.Now:HH:mm:ss}
💡 *Причина:* Отмена после принятия";

            await botClient.SendTextMessageAsync(
                chatId: chatId,
                text: notificationText,
                parseMode: ParseMode.Markdown,
                cancellationToken: cancellationToken);

            // Удаляем оригинальное сообщение с заказом
            await botClient.DeleteMessageAsync(
                chatId: callbackQuery.Message.Chat.Id,
                messageId: callbackQuery.Message.MessageId,
                cancellationToken: cancellationToken);

            // Обновляем статус заказа в базе (но НЕ отправляем в архив)
            UpdateOrderStatus(orderNumber, "cancelled", callbackQuery.From.Username);

            await botClient.AnswerCallbackQueryAsync(callbackQuery.Id, "✅ Заказ отменен", cancellationToken: cancellationToken);
        }


        // ↓↓↓ ДОБАВЛЕННЫЕ МЕТОДЫ ДЛЯ ОБРАБОТКИ ЗАКАЗОВ ↓↓↓
        private static async Task AcceptOrder(ITelegramBotClient botClient, CallbackQuery callbackQuery, int orderNumber, CancellationToken cancellationToken)
        {
            var messageId = callbackQuery.Message.MessageId;
            var chatId = callbackQuery.Message.Chat.Id;

            // Преобразуем string ID группы в long для сравнения
            long ordersGroupIdLong = long.Parse(OrdersGroupId);

            // Проверяем, что действие происходит в группе Orders
            if (chatId != ordersGroupIdLong)
            {
                await botClient.AnswerCallbackQueryAsync(
                    callbackQuery.Id,
                    "❌ Действие доступно только в группе заказов",
                    cancellationToken: cancellationToken
                );
                return;
            }

            // Загружаем информацию о заказе из базы
            var order = LoadOrderFromDatabase(orderNumber);
            if (order == null)
            {
                await botClient.AnswerCallbackQueryAsync(callbackQuery.Id, "❌ Заказ не найден", cancellationToken: cancellationToken);
                return;
            }

            // Обновляем сообщение в группе
            var originalText = callbackQuery.Message.Text;
            var updatedText = originalText.Replace("НОВЫЙ ЗАКАЗ", "ЗАКАЗ ПРИНЯТ") + $"\n\n👨‍💼 *Принял:* @{callbackQuery.From.Username}\n⏰ *Время:* {DateTime.Now:HH:mm:ss}";

            // Создаем ссылку для написания покупателю
            var contactUrl = string.IsNullOrEmpty(order.CustomerUsername)
                ? $"tg://user?id={order.CustomerId}"
                : $"https://t.me/{order.CustomerUsername}";

            var newKeyboard = new InlineKeyboardMarkup(new[]
            {
        new[]
        {
            InlineKeyboardButton.WithCallbackData("✅ Выполнено", $"complete_order_{orderNumber}"),
            InlineKeyboardButton.WithCallbackData("❌ Отменить", $"cancel_accepted_{orderNumber}")
        },
        new[]
        {
            InlineKeyboardButton.WithUrl("💬 Написать покупателю", contactUrl)
        }
    });

            await botClient.EditMessageTextAsync(
                chatId: chatId,
                messageId: messageId,
                text: updatedText,
                parseMode: ParseMode.Markdown,
                replyMarkup: newKeyboard,
                cancellationToken: cancellationToken);

            // Обновляем статус заказа в базе
            UpdateOrderStatus(orderNumber, "accepted", callbackQuery.From.Username);

            await botClient.AnswerCallbackQueryAsync(callbackQuery.Id, "✅ Заказ принят", cancellationToken: cancellationToken);
        }


        private static OrderData? LoadOrderFromDatabase(int orderNumber)
        {
            try
            {
                if (System.IO.File.Exists("orders.json"))
                {
                    var data = System.IO.File.ReadAllText("orders.json");
                    var orders = JsonSerializer.Deserialize<List<OrderData>>(data);
                    return orders?.FirstOrDefault(o => o.OrderNumber == orderNumber);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка загрузки заказа: {ex.Message}");
            }
            return null;
        }

        private static void UpdateOrderStatus(int orderNumber, string status, string acceptedBy)
        {
            try
            {
                if (System.IO.File.Exists("orders.json"))
                {
                    var data = System.IO.File.ReadAllText("orders.json");
                    var orders = JsonSerializer.Deserialize<List<OrderData>>(data) ?? new List<OrderData>();

                    var order = orders.FirstOrDefault(o => o.OrderNumber == orderNumber);
                    if (order != null)
                    {
                        order.Status = status;
                        order.AcceptedBy = acceptedBy;
                        order.AcceptedDate = DateTime.Now;

                        var json = JsonSerializer.Serialize(orders);
                        System.IO.File.WriteAllText("orders.json", json);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка обновления статуса: {ex.Message}");
            }
        }

        private static async Task DeleteOrder(ITelegramBotClient botClient, CallbackQuery callbackQuery, int orderNumber, CancellationToken cancellationToken)
        {
            var chatId = callbackQuery.Message.Chat.Id;

            // Преобразуем string ID группы в long для сравнения
            long ordersGroupIdLong = long.Parse(OrdersGroupId);

            // Проверяем, что действие происходит в группе Orders
            if (chatId != ordersGroupIdLong)
            {
                await botClient.AnswerCallbackQueryAsync(
                    callbackQuery.Id,
                    "❌ Действие доступно только в группе заказов",
                    cancellationToken: cancellationToken
                );
                return;
            }

            // Загружаем информацию о заказе из базы
            var order = LoadOrderFromDatabase(orderNumber);
            if (order == null)
            {
                await botClient.AnswerCallbackQueryAsync(callbackQuery.Id, "❌ Заказ не найден", cancellationToken: cancellationToken);
                return;
            }

            // Отправляем короткое уведомление в группу Order
            var notificationText = $@"🗑️ *Заказ #{orderNumber} удален*
❌ *Кто:* @{callbackQuery.From.Username}
⏰ *Когда:* {DateTime.Now:HH:mm:ss}";

            await botClient.SendTextMessageAsync(
                chatId: chatId,
                text: notificationText,
                parseMode: ParseMode.Markdown,
                cancellationToken: cancellationToken);

            // Удаляем оригинальное сообщение с заказом
            await botClient.DeleteMessageAsync(
                chatId: callbackQuery.Message.Chat.Id,
                messageId: callbackQuery.Message.MessageId,
                cancellationToken: cancellationToken);

            // Обновляем статус заказа в базе (но НЕ отправляем в архив)
            UpdateOrderStatus(orderNumber, "deleted", callbackQuery.From.Username);

            await botClient.AnswerCallbackQueryAsync(callbackQuery.Id, "✅ Заказ удален", cancellationToken: cancellationToken);
        }

        private static async Task CompleteOrder(ITelegramBotClient botClient, CallbackQuery callbackQuery, int orderNumber, CancellationToken cancellationToken)
        {
            // Загружаем информацию о заказе из базы
            var order = LoadOrderFromDatabase(orderNumber);
            if (order == null)
            {
                await botClient.AnswerCallbackQueryAsync(callbackQuery.Id, "❌ Заказ не найден", cancellationToken: cancellationToken);
                return;
            }

            // ДОБАВЛЯЕМ ЗАКАЗ В ПРОФИЛЬ ПОКУПАТЕЛЯ
            AddOrderToUserProfile(order, callbackQuery.From.Username);

            // ТОЛЬКО НОМЕР И ДАТА (видно сразу)
            var archiveText = $@"📋 *ЗАКАЗ #{orderNumber}*
📅 *Дата выполнения:* {DateTime.Now:dd.MM.yyyy}";

            // Кнопка для подробной информации
            var archiveKeyboard = new InlineKeyboardMarkup(new[]
            {
        new[]
        {
            InlineKeyboardButton.WithCallbackData("📋 Подробная информация", $"details_{orderNumber}")
        }
    });

            // Отправляем заказ в архивную группу
            await botClient.SendTextMessageAsync(
                chatId: ArchiveGroupId,
                text: archiveText,
                parseMode: ParseMode.Markdown,
                replyMarkup: archiveKeyboard,
                cancellationToken: cancellationToken);

            // Удаляем заказ из группы заказов
            await botClient.DeleteMessageAsync(
                chatId: callbackQuery.Message.Chat.Id,
                messageId: callbackQuery.Message.MessageId,
                cancellationToken: cancellationToken);

            // Обновляем статус заказа в базе
            UpdateOrderCompletion(orderNumber, callbackQuery.From.Username);

            await botClient.AnswerCallbackQueryAsync(callbackQuery.Id, "✅ Заказ выполнен и добавлен в архив", cancellationToken: cancellationToken);
        }


        private static async Task ShowOrderDetails(ITelegramBotClient botClient, CallbackQuery callbackQuery, int orderNumber, CancellationToken cancellationToken)
        {
            var order = LoadOrderFromDatabase(orderNumber);
            if (order == null) return;

            // ВСЯ ПОДРОБНАЯ ИНФОРМАЦИЯ (скрыта под кнопкой)
            var detailsText = $@"📋 *ЗАКАЗ #{orderNumber}*
📅 *Дата выполнения:* {order.CompletionDate:dd.MM.yyyy}

💳 *Тип буста:* {(order.IsLobbyBoost ? "Через лобби (+50%)" : "Со входом в аккаунт")}
🎮 *Режим:* Союзники

📊 *Данные заказа:*
├─ Текущее звание: {order.CurrentRank}
├─ Целевое звание: {order.TargetRank}
{(order.IsLobbyBoost ? $"└─ ID аккаунта: `{order.PlayerID}`" : "└─ Данные от аккаунта: требуются")}

💰 *Стоимость:* {order.TotalPrice}₽ / {order.TotalHoney}🍯

👤 *Покупатель:* {(string.IsNullOrEmpty(order.CustomerUsername) ? "Не указан" : $"@{order.CustomerUsername}")}
👨‍💼 *Принял:* {order.AcceptedBy}
🏁 *Выполнил:* {order.CompletedBy}";

            await botClient.EditMessageTextAsync(
                chatId: callbackQuery.Message.Chat.Id,
                messageId: callbackQuery.Message.MessageId,
                text: detailsText,
                parseMode: ParseMode.Markdown,
                replyMarkup: new InlineKeyboardMarkup(new[]
                {
            new[] { InlineKeyboardButton.WithCallbackData("⬆️ Свернуть", $"collapse_{orderNumber}") }
                }),
                cancellationToken: cancellationToken);
        }



        private static async Task CollapseOrderDetails(ITelegramBotClient botClient, CallbackQuery callbackQuery, int orderNumber, CancellationToken cancellationToken)
        {
            var order = LoadOrderFromDatabase(orderNumber);
            if (order == null) return;

            // СВЕРНУТЫЙ ВИД (только номер и дата)
            var collapsedText = $@"📋 *ЗАКАЗ #{orderNumber}*
📅 *Дата выполнения:* {order.CompletionDate:dd.MM.yyyy}";

            await botClient.EditMessageTextAsync(
                chatId: callbackQuery.Message.Chat.Id,
                messageId: callbackQuery.Message.MessageId,
                text: collapsedText,
                parseMode: ParseMode.Markdown,
                replyMarkup: new InlineKeyboardMarkup(new[]
                {
            new[] { InlineKeyboardButton.WithCallbackData("📋 Подробная информация", $"details_{orderNumber}") }
                }),
                cancellationToken: cancellationToken);
        }

        private static void AddOrderToUserProfile(OrderData order, string completedBy)
        {
            try
            {
                var users = new List<UserData>();
                if (System.IO.File.Exists("users.json"))
                {
                    var data = System.IO.File.ReadAllText("users.json");
                    users = JsonSerializer.Deserialize<List<UserData>>(data) ?? new List<UserData>();
                }

                var user = users.FirstOrDefault(u => u.UserId == order.CustomerId);
                if (user != null)
                {
                    // ОБНОВЛЯЕМ username если он изменился
                    if (!string.IsNullOrEmpty(order.CustomerUsername) && order.CustomerUsername != "Не указан")
                    {
                        user.Username = order.CustomerUsername;
                    }

                    user.CompletedOrders.Add(new CompletedOrder
                    {
                        OrderNumber = order.OrderNumber,
                        CompletionDate = DateTime.Now,
                        CompletedBy = completedBy,
                        FromRank = order.CurrentRank,
                        ToRank = order.TargetRank,
                        Price = order.TotalPrice,
                        BoostType = order.IsLobbyBoost ? "Через лобби" : "Со входом"
                    });

                    var json = JsonSerializer.Serialize(users);
                    System.IO.File.WriteAllText("users.json", json);

                    Console.WriteLine($"✅ Заказ #{order.OrderNumber} добавлен в профиль пользователя {order.CustomerId}");
                }
                else
                {
                    // СОЗДАЕМ нового пользователя если не найден
                    var newUser = new UserData
                    {
                        UserId = order.CustomerId,
                        Username = order.CustomerUsername,
                        JoinDate = DateTime.Now,
                        LastActive = DateTime.Now,
                        CompletedOrders = new List<CompletedOrder>
                {
                    new CompletedOrder
                    {
                        OrderNumber = order.OrderNumber,
                        CompletionDate = DateTime.Now,
                        CompletedBy = completedBy,
                        FromRank = order.CurrentRank,
                        ToRank = order.TargetRank,
                        Price = order.TotalPrice,
                        BoostType = order.IsLobbyBoost ? "Через лобби" : "Со входом"
                    }
                }
                    };

                    users.Add(newUser);
                    var json = JsonSerializer.Serialize(users);
                    System.IO.File.WriteAllText("users.json", json);

                    Console.WriteLine($"✅ Создан новый пользователь {order.CustomerId} с заказом #{order.OrderNumber}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Ошибка добавления заказа в профиль: {ex.Message}");
            }
        }

        private static void UpdateOrderCompletion(int orderNumber, string completedBy)
        {
            try
            {
                if (System.IO.File.Exists("orders.json"))
                {
                    var data = System.IO.File.ReadAllText("orders.json");
                    var orders = JsonSerializer.Deserialize<List<OrderData>>(data) ?? new List<OrderData>();

                    var order = orders.FirstOrDefault(o => o.OrderNumber == orderNumber);
                    if (order != null)
                    {
                        order.Status = "completed";
                        order.CompletedBy = completedBy;
                        order.CompletionDate = DateTime.Now;

                        var json = JsonSerializer.Serialize(orders);
                        System.IO.File.WriteAllText("orders.json", json);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка обновления завершения заказа: {ex.Message}");
            }
        }


        private static async Task ProcessOrderConfirmation(ITelegramBotClient botClient, long chatId, CancellationToken cancellationToken)
        {
            if (!UserStates.ContainsKey(chatId) || string.IsNullOrEmpty(UserStates[chatId].CurrentRank) ||
                string.IsNullOrEmpty(UserStates[chatId].TargetRank))
            {
                await botClient.SendTextMessageAsync(chatId, "❌ Ошибка данных заказа", cancellationToken: cancellationToken);
                return;
            }

            var userState = UserStates[chatId];
            var orderNumber = orderCounter++;
            SaveOrderCounter();

            var totalPrice = CalculatePrice(userState.CurrentRank, userState.TargetRank, userState.IsLobbyBoost);
            var honey = (int)(totalPrice * 2.5);

            // СОХРАНЯЕМ username из состояния пользователя
            var customerUsername = userState.UserName ?? "Не указан";

            // Сохраняем заказ
            var orderData = new OrderData
            {
                OrderNumber = orderNumber,
                CustomerId = chatId,
                CustomerUsername = customerUsername, // Теперь всегда будет значение
                CurrentRank = userState.CurrentRank,
                TargetRank = userState.TargetRank,
                PlayerID = userState.IsLobbyBoost ? userState.PlayerID ?? 0 : 0,
                IsLobbyBoost = userState.IsLobbyBoost,
                TotalPrice = totalPrice,
                TotalHoney = honey,
                OrderDate = DateTime.Now,
                Status = "new"
            };

            // Сохраняем только в базу (УБИРАЕМ ActiveOrders)
            SaveOrderToDatabase(orderData);

            // Формируем текст заказа
            string orderText = userState.IsLobbyBoost
                ? $@"🎯 *НОВЫЙ ЗАКАЗ #{orderNumber}* 🎯
📅 *Дата:* {DateTime.Now:dd.MM.yyyy HH:mm}

💳 *Тип буста:* Через лобби (+50%)
🎮 *Режим:* Союзники

📊 *Данные заказа:*
├─ Текущее звание: {userState.CurrentRank}
├─ Целевое звание: {userState.TargetRank}
└─ ID аккаунта: `{userState.PlayerID}`

💰 *Стоимость:* {totalPrice}₽ / {honey}🍯

👤 *Покупатель:* {(string.IsNullOrEmpty(userState.UserName) ? "Не указан" : $"@{userState.UserName}")}"
                : $@"🎯 *НОВЫЙ ЗАКАЗ #{orderNumber}* 🎯
📅 *Дата:* {DateTime.Now:dd.MM.yyyy HH:mm}

💳 *Тип буста:* Со входом в аккаунт
🎮 *Режим:* Союзники

📊 *Данные заказа:*
├─ Текущее звание: {userState.CurrentRank}
├─ Целевое звание: {userState.TargetRank}
└─ *Требуются данные от аккаунта*

💰 *Стоимость:* {totalPrice}₽ / {honey}🍯

👤 *Покупатель:* {(string.IsNullOrEmpty(userState.UserName) ? "Не указан" : $"@{userState.UserName}")}";

            var orderKeyboard = new InlineKeyboardMarkup(new[]
            {
        new[]
        {
            InlineKeyboardButton.WithCallbackData("✅ Принять заказ", $"accept_order_{orderNumber}"),
            InlineKeyboardButton.WithCallbackData("❌ Удалить", $"delete_order_{orderNumber}")
        }
    });

            try
            {
                // Отправляем заказ в группу продавцов
                var message = await botClient.SendTextMessageAsync(
                    chatId: OrdersGroupId,
                    text: orderText,
                    parseMode: ParseMode.Markdown,
                    replyMarkup: orderKeyboard,
                    cancellationToken: cancellationToken);

                // Сообщение покупателю
                string userMessage = userState.IsLobbyBoost
                    ? $@"✅ *ЗАКАЗ УСПЕШНО ОФОРМЛЕН!* 🎉

📋 *Детали заказа:*
├─ Номер: #{orderNumber}
├─ Режим: Союзники
├─ Тип: Через лобби
├─ Ранг: {userState.CurrentRank} → {userState.TargetRank}
├─ ID аккаунта: `{userState.PlayerID}`
└─ Стоимость: {totalPrice}₽ / {honey}🍯

⏳ *Ожидайте подтверждения заказа продавцом*
📞 *С вами свяжутся в ближайшее время*"
                    : $@"✅ *ЗАКАЗ УСПЕШНО ОФОРМЛЕН!* 🎉

📋 *Детали заказа:*
├─ Номер: #{orderNumber}
├─ Режим: Союзники
├─ Тип: Со входом в аккаунт
├─ Ранг: {userState.CurrentRank} → {userState.TargetRank}
└─ Стоимость: {totalPrice}₽ / {honey}🍯

⚠️ *Для выполнения заказа потребуются данные от аккаунта*

⏳ *Ожидайте подтверждения заказа продавцом*
📞 *С вами свяжутся в ближайшее время*";

                await botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: userMessage,
                    parseMode: ParseMode.Markdown,
                    replyMarkup: GetMainMenuKeyboard(),
                    cancellationToken: cancellationToken);

            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка отправки заказа: {ex.Message}");

                // Сообщение об ошибке
                await botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: @"⚠️ *Заказ оформлен, но возникла ошибка при отправке продавцу!*

📞 *Пожалуйста, свяжитесь с @krizzly2150 напрямую*",
                    parseMode: ParseMode.Markdown,
                    cancellationToken: cancellationToken);
            }

            // Сбрасываем состояние
            userState.CurrentState = UserStateState.MainMenu;
            userState.CurrentRank = null;
            userState.TargetRank = null;
            userState.PlayerID = null;
            userState.IsLobbyBoost = false;
        }
        private static void SaveOrderToDatabase(OrderData orderData)
        {
            var orders = new List<OrderData>();
            if (System.IO.File.Exists("orders.json"))
            {
                var data = System.IO.File.ReadAllText("orders.json");
                orders = JsonSerializer.Deserialize<List<OrderData>>(data) ?? new List<OrderData>();
            }

            orders.Add(orderData);
            var json = JsonSerializer.Serialize(orders);
            System.IO.File.WriteAllText("orders.json", json);
        }

        private static Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
        {
            var errorMessage = exception switch
            {
                ApiRequestException apiRequestException => $"Telegram API Error: [{apiRequestException.ErrorCode}] {apiRequestException.Message}",
                _ => exception.ToString()
            };

            Console.WriteLine(errorMessage);
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
            }

            public Task StopAsync(CancellationToken cancellationToken)
            {
                _cts?.Cancel();
                return Task.CompletedTask;
            }
        }

        public class UserState
        {
            public UserStateState CurrentState { get; set; }
            public string? SelectedMode { get; set; }
            public string? CurrentRank { get; set; }
            public string? TargetRank { get; set; }
            public long? PlayerID { get; set; }
            public bool IsLobbyBoost { get; set; }
            public string? UserName { get; set; }
        }

        public enum UserStateState
        {
            MainMenu,
            ChoosingCurrentRank,
            ChoosingTargetRank,
            WaitingForPlayerID,
            Confirmation
        }

        public class UserData
        {
            public long UserId { get; set; }
            public string? Username { get; set; }
            public DateTime JoinDate { get; set; }
            public DateTime LastActive { get; set; }
            public List<CompletedOrder> CompletedOrders { get; set; } = new List<CompletedOrder>();
        }

        public class OrderData
        {
            public int OrderNumber { get; set; }
            public long CustomerId { get; set; }
            public string? CustomerUsername { get; set; }
            public string CurrentRank { get; set; } = null!;
            public string TargetRank { get; set; } = null!;
            public long PlayerID { get; set; } // 0 означает, что ID не требуется (для входа в аккаунт)
            public bool IsLobbyBoost { get; set; }
            public int TotalPrice { get; set; }
            public int TotalHoney { get; set; }
            public DateTime OrderDate { get; set; }
            public string Status { get; set; } = null!;
            public string? AcceptedBy { get; set; }
            public DateTime? AcceptedDate { get; set; }
            public string? CompletedBy { get; set; }
            public DateTime? CompletionDate { get; set; }
        }

        public class CompletedOrder
        {
            public int OrderNumber { get; set; }
            public DateTime CompletionDate { get; set; }
            public string CompletedBy { get; set; } = null!;
            public string FromRank { get; set; } = null!;
            public string ToRank { get; set; } = null!;
            public int Price { get; set; }
            public string BoostType { get; set; } = null!;
        }

        private static async Task ShowOrderConfirmation(ITelegramBotClient botClient, long chatId, UserState userState, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(userState.CurrentRank) || string.IsNullOrEmpty(userState.TargetRank))
            {
                await botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: "❌ Ошибка: не указаны звания",
                    cancellationToken: cancellationToken);
                return;
            }

            var totalPrice = CalculatePrice(userState.CurrentRank, userState.TargetRank, userState.IsLobbyBoost);
            var honey = (int)(totalPrice * 2.5);

            var confirmationText = $@"📋 *Проверьте данные заказа:*

🎮 *Режим:* Союзники
💳 *Тип:* Со входом в аккаунт

📊 *Текущее звание:* {userState.CurrentRank}
⭐ *Целевое звание:* {userState.TargetRank}

💵 *Итоговая стоимость:* {totalPrice}₽ / {honey}🍯

⚠️ *Для буста со входом в аккаунт потребуются данные от аккаунта*";

            await botClient.SendTextMessageAsync(
                chatId: chatId,
                text: confirmationText,
                parseMode: ParseMode.Markdown,
                replyMarkup: new InlineKeyboardMarkup(new[]
                {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("✅ Подтвердить", "confirm_order"),
                InlineKeyboardButton.WithCallbackData("❌ Отменить", "cancel_order")
            }
                }),
                cancellationToken: cancellationToken);
        }
    }
}
