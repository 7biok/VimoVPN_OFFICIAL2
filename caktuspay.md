Документация API

Создание платежа
Метод для создания платежа и получения ссылки на страницу оплаты.

Запрос
POST
https://lk.cactuspay.pro/api/?method=create
Формат body: application/json
Параметры body
Имя	Тип	Описание
tokenОбязательное	STRING	Ключ вашего магазина, держите его в секрете
amountОбязательное	STRING / NUMBER	Сумма платежа
order_id	STRING	Уникальный номер платежа в вашей системе. Если не передан, будет сгенерирован автоматически
description	STRING	Описание платежа
redirect_url	STRING	Ссылка для возврата пользователя после оплаты
h2h	BOOLEAN	Если `true`, в ответе могут сразу вернуться реквизиты для оплаты
user_ip	STRING	IP пользователя. Обязательно при `h2h: true`
method	STRING	Метод для h2h-запроса реквизитов. Если не передан, используется `card`
Что важно учитывать
Поле `token` обязательно, иначе API вернёт ошибку `Поле token пустое`.
Минимальная сумма платежа: `100`.
Если передать уже существующий `order_id`, новый платёж не создаётся, а API вернёт ссылку на уже существующую страницу оплаты.
При `h2h: true` поле `user_ip` обязательно.
Поле `method` влияет на формат `requisite.response` только при `h2h: true`.
Доступные значения method
method	Тип реквизитов	Что обычно приходит в ответе
card	Перевод по карте	Номер карты, иногда имя получателя и банк
sbp	Перевод по СБП	Телефон получателя, банк, иногда имя
nspk	QR-оплата	Ссылка или QR для оплаты
vietnam_qr	QR-оплата	Ссылка или QR для оплаты
link	Платёжная ссылка	Прямая ссылка на оплату
usdt-trc20	Криптоплатёж	Адрес кошелька, сеть, валюта и сумма
trx-trc20	Криптоплатёж	Адрес кошелька, сеть, валюта и сумма
Пример успешного ответа без h2h:

{
  
"status": 
"success",
  
"response": 
{
  
  
"url": 
"https://pay.cactuspay.pro/...",
  
  
"request_check": 
false
  
}
}
Пример успешного ответа с h2h:

{
  
"status": 
"success",
  
"response": 
{
  
  
"url": 
"https://pay.cactuspay.pro/...",
  
  
"request_check": 
false,
  
  
"requisite": 
{...} // 2 items
  
}
}
Возможные ошибки:

{
  
"status": 
"error",
  
"response": 
"Сумма должна быть не менее 100"
}
Структура ответа
Поле	Тип	Описание
status	STRING	Статус запроса: `success` или `error`
response.url	STRING	Ссылка на страницу оплаты
response.request_check	BOOLEAN	Дополнительный флаг проверки, который может возвращаться вместе с реквизитами
response.requisite	OBJECT	Результат запроса реквизитов при `h2h: true`
Варианты requisite.response
Точная структура зависит от выбранного метода оплаты. Ниже перечислены основные варианты ответа.

1. card
{
  
"id": 
"transaction_id",
  
"amount": 
1500,
  
"until": 
"Sun, 06 Apr 2026 14:00:00 +0300",
  
"until_timestamp": 
1775473200,
  
"cardNumber": 
"2200123412341234",
  
"receiverName": 
"IVAN IVANOV",
  
"receiverBank": 
"Sberbank"
}
2. sbp
{
  
"id": 
"transaction_id",
  
"amount": 
1500,
  
"until": 
"Sun, 06 Apr 2026 14:00:00 +0300",
  
"until_timestamp": 
1775473200,
  
"receiverPhone": 
"+79990000000",
  
"receiverName": 
"IVAN IVANOV",
  
"receiverBank": 
"Sberbank"
}
3. nspk / vietnam_qr
{
  
"id": 
"transaction_id",
  
"amount": 
1500,
  
"until": 
"Sun, 06 Apr 2026 14:00:00 +0300",
  
"until_timestamp": 
1775473200,
  
"receiverQR": 
"https://pay.example/qr/...",
  
"qr_code": 
"raw_qr_value"
}
4. link
{
  
"id": 
"transaction_id",
  
"amount": 
1500,
  
"until": 
"Sun, 06 Apr 2026 14:00:00 +0300",
  
"until_timestamp": 
1775473200,
  
"paymentLink": 
"https://pay.example/invoice/..."
}
5. crypto
{
  
"id": 
123,
  
"wallet": 
"TRON_WALLET_ADDRESS",
  
"name": 
"USDT TRC20",
  
"network": 
"TRC20",
  
"currency": 
"USDT",
  
"any": 
"USDT",
  
"amount": 
23.5,
  
"accountNumber": 
"TRON_WALLET_ADDRESS",
  
"until": 
"Sun, 06 Apr 2026 14:00:00 +0300",
  
"until_timestamp": 
1775473200
}
Информация по платежу
Проверка статуса и данных платежа по `order_id`.

Запрос
POST
https://lk.cactuspay.pro/api/?method=get
Формат body: application/json
Параметры body
Имя	Тип	Описание
tokenОбязательное	STRING	Ключ вашего магазина, держите его в секрете
order_idОбязательное	STRING	Уникальный номер платежа в вашей системе
Структура ответа
Имя	Тип	Описание
id	NUMBER	Уникальный номер платежа в системе CactusPay
order_id	STRING	Уникальный номер платежа в вашей системе
amount	STRING	Сумма платежа
total_amount	STRING	Итоговая сумма платежа с учётом фактических данных по платёжной операции
status	STRING	Статус платежа: `ACCEPT`, `WAIT`
profit	STRING	Сумма прибыли по платежу. Если данных нет, возвращается `0`
Пример успешного ответа:

{
  
"status": 
"success",
  
"response": 
{
  
  
"id": 
12345,
  
  
"order_id": 
"ORDER_1001",
  
  
"amount": 
"1500",
  
  
"total_amount": 
"1500",
  
  
"status": 
"WAIT",
  
  
"profit": 
"0"
  
}
}
Возможные ошибки:

{
  
"status": 
"error",
  
"response": 
"Такого платежа не существует"
}
Webhook
События, которые CactusPay отправляет на ваш сервер после изменения статуса платежа.

Webhook используется для автоматической синхронизации статусов между CactusPay и вашей системой.

После успешной оплаты CactusPay отправляет `POST`-запрос на URL, который вы указали в настройках магазина.

Формат запроса
POST
URL webhook вашего магазина
Формат body: application/x-www-form-urlencoded
Поля webhook
Имя	Тип	Описание
id	NUMBER / STRING	Внутренний ID платежа в CactusPay
order_id	STRING	Номер платежа в вашей системе
amount	STRING / NUMBER	Сумма платежа
total_amount	STRING / NUMBER	Итоговая сумма платежа
status	STRING	Текущий статус платежа. В автоматическом webhook сейчас отправляется `ACCEPT`
type	STRING	Тип платежа
email_required	STRING / NUMBER	Признак обязательного email-поля
profit	STRING	Сумма прибыли
profit_rub	STRING / NUMBER	Сумма прибыли в рублях
rate	STRING / NUMBER	Курс конвертации
payment_type	STRING / NUMBER	Тип платёжной операции
description	STRING	Описание платежа
Пример payload:

{
  
"id": 
12345,
  
"order_id": 
"ORDER_1001",
  
"amount": 
"1500",
  
"total_amount": 
1500,
  
"status": 
"ACCEPT",
  
"type": 
"ONE-TIME",
  
"email_required": 
0,
  
"profit": 
"0.0000",
  
"profit_rub": 
0,
  
"rate": 
96.42,
  
"payment_type": 
0,
  
"description": 
"Оплата заказа"
}
Что важно проверить
Ваш endpoint должен принимать `POST`-запросы.
Формат данных: `application/x-www-form-urlencoded`.
Обрабатывайте повторные уведомления идемпотентно, используя `order_id`.
После получения webhook обязательно дополнительно запросите `Информация по платежу`, чтобы повторно проверить актуальный статус платежа.
После получения статуса `ACCEPT` переводите заказ в оплаченный статус.
Не полагайтесь только на входящий webhook при изменении статуса заказа.
Если endpoint временно недоступен, добавьте логи и ручную проверку через метод получения информации по платежу.
Экспорт платежей
Выгрузка платежей и агрегированной статистики за период.

Запрос
POST
https://lk.cactuspay.pro/client/shop/export
Формат body: application/json
Параметры body
Имя	Тип	Описание
tokenОбязательное	STRING	Ключ вашего магазина, держите его в секрете
dateFromОбязательное	NUMBER / STRING	Дата начала в UNIX или ISO 8601
dateToОбязательное	NUMBER / STRING	Дата окончания в UNIX или ISO 8601
Пример ответа:

{
  
"status": 
"success",
  
"response": 
{
  
  
"payments": 
[...], // 0 items
  
  
"statistics": 
{...} // 3 items
  
}
}
Формат элемента в `payments`
{
  
"id": 
0,
  
"type": 
"ONE-TIME",
  
"orderId": 
"***",
  
"amount": 
0,
  
"profit": 
0,
  
"formattedDate": 
"15.05.2025 15:57:34",
  
"timestampDate": 
1747313854,
  
"requisites": 
"***",
  
"rate": 
94.304,
  
"status": 
"WAIT"
}