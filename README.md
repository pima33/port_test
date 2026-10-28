# MagerCam iOS Wi-Fi MVP

Первый iOS-источник для существующего плагина MagerCam в OBS: задняя камера iPhone → H.264 720p30 → Wi-Fi.

**[Сборка, установка и подключение к OBS](ios/README.md)**

GitHub Actions: **iOS build and protocol tests** проверяет исходники; **iOS signed IPA** создаёт подписанную сборку и при явном выборе загружает её в TestFlight.

Нужны iPhone с iOS 17+, одна локальная сеть с ПК и установленный плагин MagerCam для OBS. Для установки на iPhone нужны Apple Developer signing assets. Первая версия передаёт видео без звука и работает при открытом приложении.

Исходный протокол: [Magerko/MagerCam](https://github.com/Magerko/MagerCam/tree/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64).
