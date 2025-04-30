# MediaPresence

[![Latest Release](https://img.shields.io/github/v/release/RaspizDIYs/CustomRPC?label=latest%20release)](https://github.com/RaspizDIYs/CustomRPC/releases/latest)
[![GitHub All Releases](https://img.shields.io/github/downloads/RaspizDIYs/CustomRPC/total)](https://github.com/RaspizDIYs/CustomRPC/releases)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

Небольшая программа для отслеживания медиа активностей из различных источников и трансляции их в статус Discord.

## ✨ Возможности

*   **Интеграция с Discord:** Автоматически отображает вашу текущую медиа активность (музыка, видео) в статусе Discord (Rich Presence), включая:
    *   Название трека/видео и исполнителя/канала.
    *   Статус воспроизведения (играет/пауза).
    *   Обложку альбома/видео (см. ниже).
    *   Настраиваемые кнопки-ссылки (до 2 шт.) на стриминговые сервисы (Spotify, YouTube Music, Apple Music, Yandex Music, Deezer, VK Music) или страницу проекта.
*   **Управление обложками:**
    *   Автоматическая загрузка обложек из Spotify или Deezer.
    *   Возможность отключить загрузку обложек.
    *   Возможность задать собственный URL для обложки по умолчанию, если основная не найдена.
*   **Пользовательский интерфейс:**
    *   Выбор активного источника для отслеживания.
    *   Управление подключениями к Spotify/Deezer/Last.fm.
    *   Настройка отображения обложек и кнопок-ссылок.
    *   Светлая/темная тема оформления.
    *   Работа в фоновом режиме с иконкой в трее.
    *   Настройки запуска (свернутым в трей, авто-подключение при старте).
*   **Автообновление:** Использует Velopack для простой установки и автоматических обновлений.

## 🛠️ Как использовать

1.  [Скачайте установщик (`MediaToRPC-win-Setup.exe`)](https://github.com/RaspizDIYs/CustomRPC/releases/latest/download/MediaToRPC-win-Setup.exe) из последнего релиза.
2.  Запустите скачанный файл (`MediaToRPC-win-Setup.exe`) для установки приложения. Velopack автоматически обработает установку и обновления.
3.  Приложение (`CustomMediaRPC.exe`) должно запуститься автоматически после установки.
4.  После запуска настройте подключения к нужным сервисам через интерфейс приложения.

## 🏗️ Сборка из исходного кода

1.  Клонируйте репозиторий: `git clone https://github.com/RaspizDIYs/CustomRPC.git` (Замените URL, если он отличается)
2.  Перейдите в директорию проекта: `cd CustomMediaRPC`
3.  Восстановите зависимости и соберите проект: `dotnet build -c Release`
4.  Исполняемый файл можно найти в `CustomMediaRPC/bin/Release/netX.Y-windows.../` (где `netX.Y` - версия целевой платформы, например `net9.0`).
5.  Для создания релизных пакетов (включая установщик) используйте скрипт `release.ps1` (убедитесь, что необходимые инструменты, такие как `vpk` и `gh`, установлены и настроены).