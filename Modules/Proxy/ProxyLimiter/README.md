1. Старт видео, перемотка<br>
~50req  за 10s pornhub<br>
~100req за 15s xhamster

2. Просмотр 1req в 3-4s<br>
#EXTINF: 3-4s
<br><br>
3. Лимиты на одно пользователя с учетом запаса
* 100 req за 10s
* 300 req за 60s
* 2000 req в час (3600s)

```json
"ProxyLimiter": [
  {
    "PermitLimit": 100,
    "Window": 10,
    "SegmentsPerWindow": 10,
    "QueueLimit": 0
  },
  {
    "PermitLimit": 300,
    "Window": 60,
    "SegmentsPerWindow": 60,
    "QueueLimit": 0
  },
  {
    "PermitLimit": 2000,
    "Window": 3600,
    "SegmentsPerWindow": 60,
    "QueueLimit": 0
  }
]
````