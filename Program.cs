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
        private const string OrdersChannelId = "@your_orders_channel"; // Замените на username/ID вашего канала

        // Состояния пользователей
        private static readonly Dictionary<long, UserState> UserStates = new();

        public static async Task Main(string[] args)
        {
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

        // Главное меню (под скрепкой)
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

        // Клавиатура выбора звания (Бронза)
        private static InlineKeyboardMarkup GetBronzeRanksKeyboard()
        {
            return new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("🥉 Бронза 1", "rank_bronze1") },
                new[] { InlineKeyboardButton.WithCallbackData("🥉 Бронза 2", "rank_bronze2") },
                new[] { InlineKeyboardButton.WithCallbackData("🥉 Бронза 3", "rank_bronze3") },
                new[] { InlineKeyboardButton.WithCallbackData("🥉 Бронза 4", "rank_bronze4") },
                new[] { InlineKeyboardButton.WithCallbackData("➡️ Далее", "ranks_next_silver") }
            });
        }

        // Клавиатура выбора звания (Серебро)
        private static InlineKeyboardMarkup GetSilverRanksKeyboard()
        {
            return new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("🥈 Серебро 1", "rank_silver1") },
                new[] { InlineKeyboardButton.WithCallbackData("🥈 Серебро 2", "rank_silver2") },
                new[] { InlineKeyboardButton.WithCallbackData("🥈 Серебро 3", "rank_silver3") },
                new[] { InlineKeyboardButton.WithCallbackData("🥈 Серебро 4", "rank_silver4") },
                new[] { 
                    InlineKeyboardButton.WithCallbackData("⬅️ Назад", "ranks_prev_bronze"),
                    InlineKeyboardButton.WithCallbackData("➡️ Далее", "ranks_next_gold")
                }
            });
        }

        // Клавиатура выбора звания (Золото)
        private static InlineKeyboardMarkup GetGoldRanksKeyboard()
        {
            return new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("🥇 Золото 1", "rank_gold1") },
                new[] { InlineKeyboardButton.WithCallbackData("🥇 Золото 2", "rank_gold2") },
                new[] { InlineKeyboardButton.WithCallbackData("🥇 Золото 3", "rank_gold3") },
                new[] { InlineKeyboardButton.WithCallbackData("🥇 Золото 4", "rank_gold4") },
                new[] { 
                    InlineKeyboardButton.WithCallbackData("⬅️ Назад", "ranks_prev_silver"),
                    InlineKeyboardButton.WithCallbackData("➡️ Далее", "ranks_next_higher")
                }
            });
        }

        // Клавиатура выбора звания (Высшие звания)
        private static InlineKeyboardMarkup GetHigherRanksKeyboard()
        {
            return new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("🔥 Феникс", "rank_phoenix") },
                new[] { InlineKeyboardButton.WithCallbackData("🎯 Ренжер", "rank_ranger") },
                new[] { InlineKeyboardButton.WithCallbackData("🏆 Чемпион", "rank_champion") },
                new[] { InlineKeyboardButton.WithCallbackData("⭐ Мастер", "rank_master") },
                new[] { InlineKeyboardButton.WithCallbackData("👑 Элита", "rank_elite") },
                new[] { InlineKeyboardButton.WithCallbackData("🐉 Легенда", "rank_legend") },
                new[] { InlineKeyboardButton.WithCallbackData("⬅️ Назад", "ranks_prev_gold") }
            });
        }

        // Клавиатура подтверждения заказа
        private static InlineKeyboardMarkup GetConfirmationKeyboard()
        {
            return new InlineKeyboardMarkup(new[]
            {
                new[] { 
                    InlineKeyboardButton.WithCallbackData("✅ Да, всё верно", "confirm_yes"),
                    InlineKeyboardButton.WithCallbackData("❌ Отмена", "confirm_no")
                }
            });
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
                    // Обработка данных от пользователя в зависимости от состояния
                    switch (userState.CurrentState)
                    {
                        case UserStateState.WaitingForMMR:
                            await ProcessMMRInput(botClient, chatId, text, userState, cancellationToken);
                            break;
                            
                        case UserStateState.WaitingForID:
                            await ProcessIDInput(botClient, chatId, text, userState, cancellationToken);
                            break;
                            
                        default:
                            await botClient.SendTextMessageAsync(
                                chatId: chatId,
                                text: "Пожалуйста, используйте кнопки меню для навигации 📋",
                                replyMarkup: GetMainMenuKeyboard(),
                                cancellationToken: cancellationToken);
                            break;
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

        private static async Task ProcessMMRInput(ITelegramBotClient botClient, long chatId, string text, UserState userState, CancellationToken cancellationToken)
        {
            // Проверяем, что введено число
            if (!int.TryParse(text, out int mmr))
            {
                await botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: "❌ *Некорректный ввод!*\n\n" +
                         "Пожалуйста, введите только число без букв, символов или эмодзи.\n\n" +
                         "📝 *Пример:* 1250",
                    parseMode: ParseMode.Markdown,
                    cancellationToken: cancellationToken);
                return;
            }

            // Проверяем диапазон MMR
            if (mmr < 0)
            {
                await botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: "❌ *Некорректный MMR!*\n\n" +
                         "MMR не может быть отрицательным числом.\n\n" +
                         "📊 *Допустимый диапазон:* от 0 до 2100",
                    parseMode: ParseMode.Markdown,
                    cancellationToken: cancellationToken);
                return;
            }

            if (mmr > 2100)
            {
                await botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: "❌ *Превышен лимит MMR!*\n\n" +
                         "Наш буст-сервис выполняет буст только до 2100 MMR.\n\n" +
                         "📊 *Допустимый диапазон:* от 0 до 2100",
                    parseMode: ParseMode.Markdown,
                    cancellationToken: cancellationToken);
                return;
            }

            // Сохраняем MMR и переходим к выбору звания
            userState.CurrentMMR = mmr;
            userState.CurrentState = UserStateState.ChoosingRank;
            
            await botClient.SendTextMessageAsync(
                chatId: chatId,
                text: $"✅ *Ваш текущий MMR: {mmr} принят!*\n\n" +
                     "Теперь выберите желаемое звание:",
                parseMode: ParseMode.Markdown,
                replyMarkup: GetBronzeRanksKeyboard(),
                cancellationToken: cancellationToken);
        }

        private static async Task ProcessIDInput(ITelegramBotClient botClient, long chatId, string text, UserState userState, CancellationToken cancellationToken)
        {
            // Проверяем, что введены только цифры
            if (!long.TryParse(text, out long playerId))
            {
                await botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: "❌ *Некорректный ввод!*\n\n" +
                         "ID аккаунта должен содержать только цифры без букв, символов или эмодзи.\n\n" +
                         "📝 *Пример:* 51345522",
                    parseMode: ParseMode.Markdown,
                    cancellationToken: cancellationToken);
                return;
            }

            // Проверяем длину ID
            if (text.Length < 8 || text.Length > 9)
            {
                await botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: "❌ *Неверная длина ID!*\n\n" +
                         "ID аккаунта должен состоять из 8 или 9 цифр.\n\n" +
                         "📝 *Пример правильного ID:* 51345522 (8 цифр) или 513455221 (9 цифр)",
                    parseMode: ParseMode.Markdown,
                    cancellationToken: cancellationToken);
                return;
            }

            // Сохраняем ID и показываем подтверждение
            userState.PlayerID = playerId;
            userState.CurrentState = UserStateState.Confirmation;
            
            // Формируем текст для подтверждения
            var confirmationText = "📋 *Пожалуйста, проверьте ваши данные:*\n\n" +
                                 $"🎮 *Режим:* {userState.SelectedMode}\n" +
                                 $"📊 *Текущий MMR:* {userState.CurrentMMR}\n" +
                                 $"⭐ *Желаемое звание:* {userState.DesiredRank}\n" +
                                 $"🆔 *ID аккаунта:* {userState.PlayerID}\n\n" +
                                 "Всё верно?";

            await botClient.SendTextMessageAsync(
                chatId: chatId,
                text: confirmationText,
                parseMode: ParseMode.Markdown,
                replyMarkup: GetConfirmationKeyboard(),
                cancellationToken: cancellationToken);
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
                        UserStates[chatId].CurrentState = UserStateState.WaitingForMMR;

                        await botClient.EditMessageTextAsync(
                            chatId: chatId,
                            messageId: messageId,
                            text: $"🎯 *Выбран режим: {modeName}*\n\n" +
                                 "📊 *Пожалуйста, введите ваш текущий MMR:*\n\n" +
                                 "ℹ️ MMR должен быть числом от 0 до 2100\n" +
                                 "📝 *Пример:* 1250",
                            parseMode: ParseMode.Markdown,
                            cancellationToken: cancellationToken);
                        break;

                    // Навигация по званиям
                    case "ranks_next_silver":
                        await botClient.EditMessageReplyMarkupAsync(
                            chatId: chatId,
                            messageId: messageId,
                            replyMarkup: GetSilverRanksKeyboard(),
                            cancellationToken: cancellationToken);
                        break;
                        
                    case "ranks_next_gold":
                        await botClient.EditMessageReplyMarkupAsync(
                            chatId: chatId,
                            messageId: messageId,
                            replyMarkup: GetGoldRanksKeyboard(),
                            cancellationToken: cancellationToken);
                        break;
                        
                    case "ranks_next_higher":
                        await botClient.EditMessageReplyMarkupAsync(
                            chatId: chatId,
                            messageId: messageId,
                            replyMarkup: GetHigherRanksKeyboard(),
                            cancellationToken: cancellationToken);
                        break;
                        
                    case "ranks_prev_bronze":
                        await botClient.EditMessageReplyMarkupAsync(
                            chatId: chatId,
                            messageId: messageId,
                            replyMarkup: GetBronzeRanksKeyboard(),
                            cancellationToken: cancellationToken);
                        break;
                        
                    case "ranks_prev_silver":
                        await botClient.EditMessageReplyMarkupAsync(
                            chatId: chatId,
                            messageId: messageId,
                            replyMarkup: GetSilverRanksKeyboard(),
                            cancellationToken: cancellationToken);
                        break;
                        
                    case "ranks_prev_gold":
                        await botClient.EditMessageReplyMarkupAsync(
                            chatId: chatId,
                            messageId: messageId,
                            replyMarkup: GetGoldRanksKeyboard(),
                            cancellationToken: cancellationToken);
                        break;

                    // Обработка выбора звания
                    case string s when s.StartsWith("rank_"):
                        var rankName = data switch
                        {
                            "rank_bronze1" => "Бронза 1",
                            "rank_bronze2" => "Бронза 2",
                            "rank_bronze3" => "Бронза 3",
                            "rank_bronze4" => "Бронза 4",
                            "rank_silver1" => "Серебро 1",
                            "rank_silver2" => "Серебро 2",
                            "rank_silver3" => "Серебро 3",
                            "rank_silver4" => "Серебро 4",
                            "rank_gold1" => "Золото 1",
                            "rank_gold2" => "Золото 2",
                            "rank_gold3" => "Золото 3",
                            "rank_gold4" => "Золото 4",
                            "rank_phoenix" => "Феникс",
                            "rank_ranger" => "Ренжер",
                            "rank_champion" => "Чемпион",
                            "rank_master" => "Мастер",
                            "rank_elite" => "Элита",
                            "rank_legend" => "Легенда",
                            _ => "Неизвестное звание"
                        };

                        if (UserStates.ContainsKey(chatId))
                        {
                            UserStates[chatId].DesiredRank = rankName;
                            UserStates[chatId].CurrentState = UserStateState.WaitingForID;
                            
                            await botClient.EditMessageTextAsync(
                                chatId: chatId,
                                messageId: messageId,
                                text: $"✅ *Выбрано звание: {rankName}*\n\n" +
                                     "🆔 *Теперь введите ваш внутриигровой ID:*\n\n" +
                                     "ℹ️ ID должен состоять из 8 или 9 цифр\n" +
                                     "📝 *Пример:* 51345522",
                                parseMode: ParseMode.Markdown,
                                cancellationToken: cancellationToken);
                        }
                        break;

                    case "confirm_yes":
                        await ProcessOrderConfirmation(botClient, chatId, cancellationToken);
                        break;

                    case "confirm_no":
                        if (UserStates.ContainsKey(chatId))
                        {
                            UserStates[chatId].CurrentState = UserStateState.MainMenu;
                        }
                        
                        await botClient.EditMessageTextAsync(
                            chatId: chatId,
                            messageId: messageId,
                            text: "❌ *Заказ отменен.*\n\n" +
                                 "Если передумаете - всегда можете оформить новый заказ!",
                            parseMode: ParseMode.Markdown,
                            cancellationToken: cancellationToken);
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

        private static async Task ProcessOrderConfirmation(ITelegramBotClient botClient, long chatId, CancellationToken cancellationToken)
        {
            if (!UserStates.ContainsKey(chatId) || UserStates[chatId].CurrentMMR == null || 
                string.IsNullOrEmpty(UserStates[chatId].DesiredRank) || UserStates[chatId].PlayerID == null)
            {
                await botClient.SendTextMessageAsync(chatId, "❌ Ошибка данных заказа", cancellationToken: cancellationToken);
                return;
            }

            var userState = UserStates[chatId];
            var orderNumber = DateTime.Now.ToString("yyyyMMddHHmmss"); // Генерируем номер заказа

            // Формируем красивый текст для канала заказов
            var orderText = $"🛒 *ЗАКАЗ #{orderNumber}* 🛒\n\n" +
                           $"👤 *Покупатель:* {(userState.UserName != null ? "@" + userState.UserName : "Unknown")}\n" +
                           $"🆔 *ID покупателя:* {chatId}\n" +
                           $"🎮 *Режим:* {userState.SelectedMode}\n\n" +
                           $"─━─━─━─━─━─━─━─━─━─━─━─━─━─\n\n" +
                           $"📊 *Текущий MMR:* {userState.CurrentMMR}\n" +
                           $"⭐ *Желаемое звание:* {userState.DesiredRank}\n" +
                           $"🆔 *ID аккаунта:* {userState.PlayerID}\n\n" +
                           $"─━─━─━─━─━─━─━─━─━─━─━─━─━─\n\n" +
                           $"⏰ *Время:* {DateTime.Now:dd.MM.yyyy HH:mm}\n" +
                           $"🔗 *Ссылка:* [Написать покупателю](tg://user?id={chatId})";

            try
            {
                // Отправляем заказ в канал
                await botClient.SendTextMessageAsync(
                    chatId: OrdersChannelId,
                    text: orderText,
                    parseMode: ParseMode.Markdown,
                    cancellationToken: cancellationToken);

                // Логируем заказ в консоль
                Console.WriteLine($"=== НОВЫЙ ЗАКАЗ #{orderNumber} ===");
                Console.WriteLine(orderText);
                Console.WriteLine("================================");

                // Сохраняем в файл
                System.IO.File.AppendAllText("orders.log", $"\n\n{orderText}\n");

            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка отправки заказа в канал: {ex.Message}");
                System.IO.File.AppendAllText("error_orders.log", $"\nОшибка: {ex.Message}\nЗаказ: {orderText}\n");
            }

            // Сообщение для покупателя
            var userMessage = $"✅ *Заказ #{orderNumber} оформлен успешно!* 🎉\n\n" +
                             $"📞 *Свяжитесь с продавцом:* [@{SellerUsername}](https://t.me/{SellerUsername})\n\n" +
                             $"⚡ *Продавец свяжется с вами в ближайшее время для уточнения деталей и оплаты!*\n\n" +
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
            userState.CurrentMMR = null;
            userState.DesiredRank = null;
            userState.PlayerID = null;
            userState.SelectedMode = null;
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
            public string? SelectedMode { get; set; }
            public int? CurrentMMR { get; set; }
            public string? DesiredRank { get; set; }
            public long? PlayerID { get; set; }
            public string? UserName { get; set; }
        }

        public enum UserStateState
        {
            MainMenu,
            WaitingForMMR,
            ChoosingRank,
            WaitingForID,
            Confirmation
        }
    }
}
