# MagerCam iOS — первый Wi-Fi источник для OBS

Нативный SwiftUI/AVFoundation/VideoToolbox проект для iPhone с iOS 17+.
Цель первой версии: показать видео с задней камеры iPhone в **существующем источнике MagerCam в OBS** по локальной сети.

## Текущая готовность

Реализованы исходники приложения, обычный Xcode-проект, проверки протокола и workflows сборки/подписи.
**macOS CI пройден:** 4 теста протокола без ошибок, Debug для симулятора и Release для iPhone успешно собраны в Xcode 26.6 / Swift 6.3.3. [Первый успешный прогон](https://github.com/pima33/port_test/actions/runs/40748688081), исходники `18b5066`.
На Windows также проверены структура проекта, plist/scheme, генератор проекта и инструмент проверки потока.
**Установка на настоящий iPhone, работа камеры и изображение в OBS ещё не проверены.** Для практической приёмки выполните установку и проверки ниже.

## Установка с Windows без Mac и платного Apple Developer

Для этого варианта нужен обычный Apple ID, iPhone с iOS 17+ и USB-кабель. [Sideloadly](https://sideloadly.io/) поддерживает подпись IPA бесплатным Apple ID на Windows. Mac и платная подписка для этого способа не требуются.

1. В [GitHub Actions](https://github.com/pima33/port_test/actions/workflows/ios-build.yml) откройте последний успешный запуск и скачайте артефакт **MagerCam-iOS-IPA-…**. Распакуйте ZIP: внутри `MagerCam-unsigned.ipa`, контрольная сумма и сведения о сборке. Для скачивания артефактов войдите в GitHub.
2. Установите Sideloadly с официального сайта и компоненты Apple, указанные там в разделе **Before you install**. Подключите разблокированный iPhone кабелем и подтвердите доверие к компьютеру.
3. Откройте IPA в Sideloadly, выберите iPhone, введите свой Apple ID и запустите установку. Пароль и подтверждение Apple вводите самостоятельно в инструменте установки, а не в чате или GitHub.
4. На iPhone подтвердите доверие профилю в **Настройки → Основные → VPN и управление устройством**. Включите **Настройки → Конфиденциальность и безопасность → Режим разработчика**, завершите предложенную перезагрузку и подтверждение. [Инструкция Sideloadly](https://sideloadly.io/faq.html).
5. Откройте MagerCam и перейдите к подключению OBS ниже. Разрешите доступ к камере и локальной сети, если iOS запросит его.

IPA из CI не подписана: её нужно подписать при установке через Sideloadly; открытие файла в приложении «Файлы» не установит приложение. Эту установку на физическом устройстве ещё предстоит проверить.
С бесплатным Apple ID подпись действует **7 дней**; затем обновите её в Sideloadly или настройте его auto-refresh. Лимит — 3 приложения, подписанных бесплатным аккаунтом, на устройстве. [Ограничения и обновление подписи](https://sideloadly.io/faq.html).

## Что входит в MVP

- Задняя широкоугольная камера, фиксированный режим 1280×720, 30 fps.
- H.264 Main, 8-bit SDR, 4 Mbps, без B-frames, ключевой кадр каждые 2 секунды и при подключении.
- Преобразование length-prefixed VideoToolbox NAL в Annex B; SPS/PPS отдельно, PTS в микросекундах.
- TCP 4848, GET /stream, /api/status, /api/set; оригинальный MC header 16 bytes.
- Шестизначный PIN на экране, X-MagerCam-Key или key в query; ограничение неверных попыток.
- До 4 streaming-клиентов. Медленное соединение закрывается при накоплении старого видео; OBS переподключается.
- Кнопка запуска/остановки, preview, IPv4, число подключений, отображение ошибок.
- Работа на переднем плане в landscapeRight. Автоблокировка отключена во время capture; уход в фон завершает соединения, возврат возобновляет ранее запрошенную трансляцию.

Первая версия **без звука**, HEVC, discovery, QR, изменения настроек камеры, JPEG/web-preview и записи.
В /api/set параметры пока не применяются: возвращается фактическое состояние фиксированного режима. Запрос OBS с codec=hevc допускается, но сервер сообщает hevc=false и отправляет H.264 config с flags=1. AUDIO_CONFIG отправляется для сохранения порядка handshake, AUDIO-пакетов нет, audio=false.
PIN меняется при перезапуске приложения; при обычном Stop/Start остаётся прежним.

## Подключение к OBS

1. Установите подписанное приложение на iPhone. Подключите iPhone и ПК к одной обычной LAN/Wi-Fi сети (без client isolation/guest isolation).
2. Откройте приложение, держите телефон горизонтально, нажмите **Запустить камеру** и разрешите камеру.
3. В OBS нужен установленный **плагин MagerCam**, который добавляет источник **MagerCam (телефон)**. Стандартный «Медиаисточник» не понимает MC-поток.
4. Добавьте этот источник, выберите Wi-Fi, введите IPv4 с iPhone, порт **4848** и показанный PIN.
5. Выберите **1280×720 / 30 fps / H.264**. Отключите звук и удалённое управление камерой в свойствах источника. Для первого теста можно отключить аппаратное декодирование.
6. Дождитесь изображения. Проверьте поворот: если конкретная связка capture/OBS показывает поворот, используйте ручной поворот источника OBS и зафиксируйте результат для исправления ориентации. Реальная ориентация ещё требует теста iPhone.

Приложение должно оставаться открытым. При ручной блокировке телефона передача прекращается. Если Wi-Fi назначил новый IP, обновите адрес в OBS.
GET http://IP:4848/ в браузере возвращает короткую инструкцию и позволяет проверить доступность TCP; видеоплеера на этой странице нет.

## Сборка

В macOS с Xcode:

```sh
swift test --package-path ios
xcodebuild -project ios/MagerCam.xcodeproj -scheme MagerCam \
  -sdk iphonesimulator -destination 'generic/platform=iOS Simulator' \
  CODE_SIGNING_ALLOWED=NO build
```

Откройте ios/MagerCam.xcodeproj, выберите вашу Team и уникальный Bundle ID для установки из Xcode.
Проект уже сгенерирован и включён в поставку. После добавления Swift-файлов обновите его:

```sh
python3 ios/scripts/generate_project.py
```

В Windows можно редактировать файлы и запустить генератор установленным Python; для iOS SDK и codesign нужна macOS.

### GitHub Actions без локального Mac

В [pima33/port_test](https://github.com/pima33/port_test) workflow **iOS build and protocol tests** запускает Swift tests, simulator build и unsigned device build на macos-26. Он также упаковывает device build в **MagerCam-unsigned.ipa** для подписи на Windows, проверяет структуру IPA и arm64 executable и сохраняет SHA-256. Артефакты доступны 14 дней; затем workflow можно запустить заново. Не устанавливайте simulator .app на телефон.

Для подписанной в CI IPA через **платный Apple Developer Account** используйте ручной workflow **iOS signed IPA** и environment **ios-signing** (рекомендуется ограничить разрешённые ветки и добавить reviewer). Это отдельный вариант для Ad Hoc/TestFlight; для установки через Sideloadly он не нужен. Workflow подписи ещё не запускался. Перед его запуском в Apple Developer должны существовать App ID, Apple Distribution certificate с private key и подходящий provisioning profile. Введите:

| Secret | Содержимое |
|---|---|
| IOS_DISTRIBUTION_P12_BASE64 | P12 certificate + private key, base64 |
| IOS_DISTRIBUTION_P12_PASSWORD | Пароль P12 |
| IOS_PROFILE_BASE64 | App Store или Ad Hoc profile, base64 |

Inputs: зарегистрированные bundle_id и team_id, method=app-store-connect для TestFlight или release-testing для Ad Hoc. Ad Hoc profile должен содержать UDID iPhone. Не отправляйте секреты в чат и не добавляйте в Git.

Чтобы загрузить сборку в TestFlight прямо из workflow, дополнительно задайте ASC_KEY_ID, ASC_ISSUER_ID, ASC_PRIVATE_KEY_BASE64 (содержимое .p8 в base64) и выберите upload_testflight=true. Нужна запись приложения в App Store Connect и разрешения API key для upload. После обработки build назначьте тестовую группу в App Store Connect и установите через TestFlight. Учётная запись Apple Developer и ручная настройка владельца обязательны. Upload не означает, что Apple processing/review уже завершились.

Если upload_testflight=false, workflow только сохраняет подписанную IPA. App Store IPA предназначена для App Store Connect/TestFlight; её нельзя просто открыть в Files. Ad Hoc установка требует зарегистрированного устройства, подходящего канала доставки IPA и проверки Developer Mode (подробности в ../docs/ios-port-plan.md).

## Проверка реального потока с Windows

После запуска приложения:

```powershell
py ios/scripts/check_stream.py 192.168.1.23
```

Замените IP и введите PIN по запросу. Проверка использует стандартную библиотеку Python, ничего не устанавливает и не сохраняет видео. Она отправляет запрос с параметрами существующего OBS, проверяет HTTP/MC framing, config → keyframe, Annex B prefix, монотонные PTS и STATUS. Это структурная проверка, не декодирование видео.

Минимальная физическая приёмка:

- CI: Swift tests и обе сборки успешны.
- Подписанное приложение запускается на iPhone; камера и IP видны.
- check_stream.py проходит 10 секунд без ошибок.
- OBS показывает живую картинку 720p30 минимум 10 минут; нет накопления задержки или зависшего кадра.
- Закрытие/повторное открытие источника в OBS восстанавливает изображение.
- Stop/Start и background/foreground восстанавливают поток после переподключения OBS.
- Неверный PIN не даёт доступа, правильный работает после исправления/окончания rate-limit окна.

## Совместимость и файлы

Контракт сверён с Magerko/MagerCam, commit f6ca87d896ebcd8bea3eebfcb203d110a15c2d64, obs-plugin/src/magercam.c. Android/Windows/OBS не изменялись.

Sources/Protocol — формат MC, HTTP и Annex B; Sources/App — UI, камера, encoder и сервер; Tests — Swift contract tests; scripts/check_stream.py — тест реального соединения; MagerCam.xcodeproj — native Xcode project.
Это первоначальная реализация для практического испытания. Полный аудит и последующие функции описаны в ../docs/ios-port-plan.md; его прежняя отметка «реализация не начиналась» относится к дате завершения аудита.
