# VPN Notes System

Консольная система заметок и мониторинга для VPN-провайдера.

Позволяет операторам ИТ-инфраструктуры вести записи о работах и инцидентах, отслеживать состояние машин (CPU, RAM, диск) и управлять пользователями с разграничением прав по ролям.

---

## Содержание

- [Возможности](#возможности)
- [Установка](#установка)
- [Первый запуск](#первый-запуск)
- [Роли и права](#роли-и-права)
- [Команды](#команды)
  - [Заметки](#заметки)
  - [Мониторинг](#мониторинг)
  - [Администрирование](#администрирование)
  - [Общие команды](#общие-команды)
- [Обновления](#обновления)
- [Конфигурация](#конфигурация)
- [Решение проблем](#решение-проблем)

---

## Возможности

- **Заметки об инцидентах** — добавление, поиск, удаление с поддержкой тегов и привязкой к машинам
- **Мониторинг инфраструктуры** — автоматический сбор метрик CPU, RAM и диска с каждой машины
- **Управление пользователями** — три роли с разграничением прав
- **Автоматические обновления** — установка новых версий через GitHub Releases одной командой
- **Защита от SQL injection** — параметризованные запросы, строгая валидация ввода
- **Многоуровневая защита прав** — проверка в приложении + ограничения PostgreSQL

---

## Установка

### Что нужно

- Windows 10/11 (x64)
- Сетевой доступ к серверу БД

### Шаги установки

1. Скачайте последний релиз с [GitHub Releases](https://github.com/Sat1sf1ed/vpn-notes-system/releases)
2. Распакуйте архив `vpn-notes-X.Y.Z.zip` в любую папку (например, `C:\Tools\VpnNotes\`)
3. Отредактируйте `app-config.yml` — укажите адрес вашей БД (см. раздел [Конфигурация](#конфигурация))
4. Запустите `notes.exe`
5. Войдите под учётной записью, которую выдал администратор

---

## Первый запуск

При запуске программа запрашивает логин и пароль:

```
VPN Notes System v1.2.0

Username: ivan
Password: ********

Connected to database.
Logged in as: ivan (role: user)
You can work with notes. Type 'help' to see available commands.
Watcher started (PID 12345).
Type 'help' for commands, 'exit' to quit.

notes>
```

После успешного входа:
- Запускается фоновый процесс **Watcher** для сбора метрик
- В фоне проверяется наличие новых версий на GitHub
- Появляется приглашение `notes>` для ввода команд

---

## Роли и права

В системе три роли с разными правами:

| Роль | Заметки | Метрики | Машины | Управление пользователями |
|---|:---:|:---:|:---:|:---:|
| **user** | только свои | пишет (Watcher) | только чтение | ❌ |
| **stats** | только свои | ✅ | ✅ + добавление | ❌ |
| **admin** | все заметки | ✅ | ✅ | ✅ |

**Что это значит:**

- **user** — оператор технической поддержки. Работает со своими заметками о работах и инцидентах. Его машина передаёт метрики в систему, но сам он метрики не видит.
- **stats** — оператор мониторинга. Следит за состоянием инфраструктуры, регистрирует новые машины, ведёт свои заметки о замеченных аномалиях.
- **admin** — администратор. Может всё: и заметки (включая чужие), и мониторинг, и управление пользователями.

---

## Команды

### Заметки

Доступно для всех ролей. Пользователи user и stats видят только свои заметки, admin видит все.

#### `add <text> [--tag X] [--machine Y]`

Создать новую заметку.

**Параметры:**
- `<text>` — текст заметки (если содержит пробелы — в двойных кавычках)
- `--tag X` — добавить тег (можно указать несколько раз)
- `--machine Y` — привязать к машине по hostname

**Примеры:**

```
notes> add "Перезагрузил OpenVPN на gateway-01"
Note created with ID 1.

notes> add "Установил SSL сертификат" --tag ssl --tag maintenance
Note created with ID 2.

notes> add "Утечка памяти на app-server" --machine app-01 --tag incident
Note created with ID 3.
```

---

#### `list [--last N]`

Показать список заметок.

**Параметры:**
- `--last N` — показать только последние N заметок (по умолчанию 20)

**Пример:**

```
notes> list --last 5

ID  Created              Created by   Text
--- -------------------- ------------ ----------------------------------------
5   2026-05-23 10:45     ivan         Завершил миграцию БД
4   2026-05-23 10:30     ivan         Регулярная проверка серверов
3   2026-05-22 18:15     ivan         Утечка памяти на app-server [incident]
2   2026-05-22 14:20     ivan         Установил SSL сертификат [ssl][maintenance]
1   2026-05-22 09:10     ivan         Перезагрузил OpenVPN на gateway-01

```

---

#### `show <id>`

Показать детали конкретной заметки.

**Пример:**

```
notes> show 3

Note #3
  Created:      2026-05-22 18:15:42 UTC
  Created by:   ivan
  Machine:      app-01
  Tags:         incident
  
  Text:
  Утечка памяти на app-server. Перезапустил сервис, поставил мониторинг
  использования RAM на ближайшие сутки. Если повторится — нужно искать
  причину в коде.
```

---

#### `delete <id>`

Удалить заметку по ID.

**Пример:**

```
notes> delete 1
Note 1 deleted.

notes> delete 99999
Note 99999 not found.
```

---

#### `search "<query>"`

Поиск заметок по тексту (регистронезависимый).

**Пример:**

```
notes> search "openvpn"

Found 2 notes:

ID  Created              Created by       Text
--- -------------------- ----------- ----------------------------------------
1   2026-05-22 09:10     ivan        Перезагрузил OpenVPN на gateway-01
```

Поиск регистронезависимый — `openvpn`, `OPENVPN` и `OpenVPN` дают одинаковый результат.

---

### Мониторинг

Доступно для ролей: **stats**, **admin**

#### `machines`

Показать все зарегистрированные машины с их статусом.

**Статусы:**
- **online** — Watcher активен (метрики обновлялись менее 2 минут назад)
- **stale** — метрики устарели (2-10 минут)
- **offline** — машина не отвечает (более 10 минут)

**Пример:**

```
notes> machines

ID  Hostname           Last Seen          Status
--- ------------------ ------------------ ----------
1   gateway-01         15 seconds ago     online
2   app-server-01      45 seconds ago     online
3   db-server-01       5 minutes ago      stale
4   backup-server      2 hours ago        offline
```

---

#### `stats --machine <hostname>`

Показать последние метрики конкретной машины.

**Пример:**

```
notes> stats --machine gateway-01

Machine: gateway-01
Last update: 2026-05-23 10:48:30 UTC (15 seconds ago)

CPU:    23.5 %
RAM:    8 GB / 16 GB (50 %)
Disk:   45 GB / 250 GB (18 %)
```

---

#### `addmachine <hostname>`

Зарегистрировать новую машину в системе.

**Правила для hostname:**
- Длина 1-255 символов
- Только буквы, цифры, точки, дефисы, подчёркивания

**Пример:**

```
notes> addmachine new-gateway-02
Machine 'new-gateway-02' registered with ID 5.

notes> addmachine new-gateway-02
Machine 'new-gateway-02' is already registered with ID 5.

notes> addmachine invalid name
Error: Hostname can only contain letters, digits, dots, hyphens and underscores
```

После регистрации, когда оператор запустит `notes.exe` на этой машине — её Watcher начнёт передавать метрики автоматически.

---

### Администрирование

Доступно только для роли **admin**.

#### `users`

Показать всех пользователей системы.

**Пример:**

```
notes> users

Total users: 4

  admin_user
    role:       admin
    full name:  System Administrator
    created:    2026-05-20 10:00:00
    created by: system

  ivan
    role:       user
    full name:  Иван Иванов
    created:    2026-05-20 10:03:00
    created by: admin_user

  petr
    role:       stats
    full name:  Пётр Петров
    created:    2026-05-20 10:05:00
    created by: admin_user

  maria
    role:       user
    full name:  Мария Сидорова
    created:    2026-05-22 14:20:00
    created by: admin_user
```

---

#### `adduser <username> <role> [--name "Full Name"]`

Создать нового пользователя.

**Параметры:**
- `<username>` — логин (3-63 символа, только буквы, цифры, подчёркивания)
- `<role>` — роль: `user`, `stats` или `admin`
- `--name "Full Name"` — полное имя (опционально)

**Правила:**
- Минимальная длина пароля — 6 символов
- Пароль вводится при выполнении команды и подтверждается дважды

**Пример:**

```
notes> adduser sergey user --name "Сергей Кузнецов"
Enter password for sergey: ********
Confirm password: ********
User 'sergey' created with role 'user'.

notes> adduser monitoring_bot stats
Enter password for monitoring_bot: ********
Confirm password: ********
User 'monitoring_bot' created with role 'stats'.
```

---

#### `deluser <username>`

Удалить пользователя.

**Защита:** нельзя удалить самого себя.

**Пример:**

```
notes> deluser sergey
About to delete user:
  username: sergey
  role:     user
  name:     Сергей Кузнецов
Confirm deletion? (y/N): y
User 'sergey' deleted.

notes> deluser admin_user
You cannot delete your own account.
```

---

---

#### `logs [--last N] [--level X] [--date YYYY-MM-DD]`

Показать записи из журнала Watcher (только для admin).

**Параметры:**
- `--last N` — количество последних записей (по умолчанию 20, максимум 1000)
- `--level X` — фильтр по минимальному уровню важности: DEBUG, INFO, WARN, ERROR, FATAL
- `--date YYYY-MM-DD` — конкретная дата (по умолчанию сегодня)

**Примеры:**

```
notes> logs                          # последние 20 за сегодня
notes> logs --last 100               # последние 100 записей
notes> logs --level ERROR            # только ERROR и FATAL
notes> logs --date 2026-05-22        # вчерашние логи
notes> logs --level WARN --last 50   # последние 50 предупреждений и выше

```


Записи выводятся с цветовой подсветкой: красный — ошибки, жёлтый — предупреждения, серый — информация.

---

### Общие команды

Доступно для всех ролей.

#### `update`

Проверить наличие обновлений и установить новую версию.

**Пример (есть обновление):**

```
notes> update
New version available: 1.3.0
Download size: 65 MB
Proceed with installation? (y/N): y

Downloading...
Extracting archive...
Launching updater. The application will restart automatically.
```

После этого окно программы закроется, появится окно Updater, который заменит файлы и запустит новую версию.

**Пример (обновлений нет):**

```
notes> update
Checking for updates...
You are on the latest version.
```

При запуске программы проверка обновлений происходит **в фоне** автоматически. Если новая версия найдена — появится уведомление:

```
*** New version 1.3.0 available. Run 'update' to install. ***
```

---

#### `help`

Показать справку по командам. Состав справки зависит от роли пользователя.

**Пример (под админом):**

```
notes> help

Available commands:

  --- Notes ---
  add <text> [--tag X] [--machine Y]    Create a new note
  list [--last N]                       Show notes (default last 20)
  show <id>                             Show note details
  delete <id>                           Delete a note
  search "<query>"                      Search notes by text

  --- Monitoring ---
  machines                              Show all machines with status
  stats --machine <hostname>            Show latest metrics for a machine
  addmachine <hostname>                 Register a new machine

  --- Administration ---
  users                                 Show all users in the system
  adduser <username> <role> [--name X]  Create a new user
                                        Roles: user, stats, admin
  deluser <username>                    Delete a user

  --- Common ---
  update                                Check and install update from GitHub
  help                                  Show this help
  exit                                  Exit the application

Wrap text with spaces in double quotes: add "Hello world"
```

---

#### `exit` / `quit`

Выйти из программы. Watcher завершится автоматически.

```
notes> exit
Shutting down...
Watcher stopped.
Goodbye.
```

---

## Обновления

Система автоматически проверяет обновления при запуске и периодически в фоне. Если найдена новая версия — выводится уведомление в строке приглашения.

Установка обновления:

1. Введите команду `update`
2. Подтвердите установку (y)
3. Программа скачает архив с GitHub Releases
4. Запустится Updater и заменит файлы
5. Программа автоматически перезапустится в новой версии

**Важно:**
- Во время обновления Watcher также перезапускается
- Пользовательский `app-config.yml` НЕ перезаписывается — ваши настройки сохраняются
- Если обновление прошло неудачно — текущая версия остаётся рабочей

---

## Конфигурация

Файл `app-config.yml` лежит рядом с `notes.exe`. Содержит настройки подключения к БД и параметры обновлений.

```yaml
database:
  host: "localhost"                  # IP или hostname сервера БД
  port: 5432                         # порт PostgreSQL
  database: "vpnnotes"               # имя базы данных

watcher:
  metrics_interval_seconds: 60       # интервал сбора метрик (секунды)

update:
  current_version: "1.2.0"           # текущая установленная версия (обновляется автоматически)
  github:
    owner: "Sat1sf1ed"              # владелец GitHub-репозитория
    repo: "vpn-notes-system"        # название репозитория
```

После изменения файла перезапустите `notes.exe`.

---

## Решение проблем

### При запуске: "Login failed: 28P01: password authentication failed"

Неверный логин или пароль. Уточните у администратора.

### При запуске: "Login failed: timeout expired"

Не удаётся подключиться к серверу БД. Проверьте:
- Доступность сервера (ping)
- Правильность `host` и `port` в `app-config.yml`
- Открыт ли порт 5432 в файрволе

### "Command '...' is not allowed for role '...'"

Команда недоступна для вашей роли. Используйте `help` чтобы увидеть доступные команды. Если нужны дополнительные права — обратитесь к администратору.

### "Update failed: HttpRequestException"

Нет интернета или GitHub недоступен. Проверьте сетевое подключение и попробуйте позже.

### Watcher не запускается

В Task Manager должен быть процесс `VpnNotes.Watcher.exe`. Если его нет:
- Проверьте, что у вас есть права записи метрик (см. роль)
- Посмотрите логи в папке `logs/` рядом с `notes.exe`

### Machine show "offline", хотя машина работает

Проверьте, что на той машине запущен `notes.exe` с активным пользователем. Watcher работает только пока пользователь залогинен. Также может быть проблема с подключением к БД — посмотрите логи Watcher.

### Машина не находится в `machines`

Машина не зарегистрирована. Стат-оператор или админ должен выполнить команду `addmachine <hostname>`.

### "Failed to create user: 42501"

У текущего администратора нет нужных прав в PostgreSQL.

---

## Шпаргалка по командам

```
═══ ЗАМЕТКИ (user, admin, stats) ═══════════════════════════════════════════

add "text" [--tag X] [--machine Y]    Создать заметку
list [--last N]                       Показать заметки
show <id>                             Детали заметки
delete <id>                           Удалить заметку
search "query"                        Поиск по тексту

═══ МОНИТОРИНГ (stats, admin) ════════════════════════════════════════

machines                              Список машин
stats --machine <hostname>            Метрики машины
addmachine <hostname>                 Зарегистрировать машину

═══ АДМИНИСТРИРОВАНИЕ (admin) ════════════════════════════════════════

users                                 Список пользователей
adduser <username> <role> [--name X]  Создать пользователя
deluser <username>                    Удалить пользователя

═══ ОБЩИЕ (все роли) ═════════════════════════════════════════════════

update                                Обновить программу
help                                  Справка
exit                                  Выход
```

---

## Версия

Текущая версия системы отображается при запуске и в заголовке окна. Также её можно посмотреть в `app-config.yml` (поле `update.current_version`).

История релизов: https://github.com/Sat1sf1ed/vpn-notes-system/releases
