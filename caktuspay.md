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