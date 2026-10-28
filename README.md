# MagerCam iOS Wi-Fi MVP

Первый iOS-источник для существующего плагина MagerCam в OBS: задняя камера iPhone → H.264 720p30 → Wi-Fi.

**[Сборка, установка и подключение к OBS](ios/README.md)**

GitHub Actions: **iOS build and protocol tests** проверяет исходники; **iOS signed IPA** создаёт подписанную сборку и при явном выборе загружает её в TestFlight.

[Тесты и обе сборки прошли](https://github.com/pima33/port_test/actions/runs/40748688081). Проверка камеры на настоящем iPhone и изображения в OBS ещё предстоит.

Нужны iPhone с iOS 17+, одна локальная сеть с ПК и установленный плагин MagerCam для OBS. **Можно установить с Windows без Mac и платного Apple Developer:** CI выдаёт неподписанную IPA для подписи обычным Apple ID через Sideloadly. Подробные шаги и ограничения — в инструкции выше. Первая версия передаёт видео без звука и работает при открытом приложении.

Исходный протокол: [Magerko/MagerCam](https://github.com/Magerko/MagerCam/tree/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64).
