const http = require("http");
const fs = require("fs");
const path = require("path");
const { Server } = require("socket.io");

// ------------------------------------------------------------
// MIME-типы для статики
// ------------------------------------------------------------
const MIME = {
    ".html": "text/html; charset=utf-8",
    ".js":   "application/javascript; charset=utf-8",
    ".css":  "text/css; charset=utf-8",
    ".mp3":  "audio/mpeg",
    ".wav":  "audio/wav",
    ".ogg":  "audio/ogg",
    ".png":  "image/png",
    ".jpg":  "image/jpeg",
    ".jpeg": "image/jpeg",
    ".gif":  "image/gif",
    ".svg":  "image/svg+xml",
    ".ico":  "image/x-icon",
    ".json": "application/json; charset=utf-8"
};

// ------------------------------------------------------------
// HTTP-сервер: отдаёт index.html по "/" и статику по имени файла
// ------------------------------------------------------------
const httpServer = http.createServer((req, res) => {
    let urlPath = decodeURIComponent(req.url.split("?")[0]);
    if (urlPath === "/" || urlPath === "") urlPath = "/index.html";

    // Защита от обхода каталога (path traversal)
    const safeRelative = path.normalize(urlPath).replace(/^([.][.][\/\\])+/, "");
    const filePath = path.join(__dirname, safeRelative);

    if (!filePath.startsWith(__dirname)) {
        res.writeHead(403, { "Content-Type": "text/plain; charset=utf-8" });
        res.end("Forbidden");
        return;
    }

    fs.readFile(filePath, (err, data) => {
        if (err) {
            res.writeHead(404, { "Content-Type": "text/plain; charset=utf-8" });
            res.end("Not found: " + safeRelative);
            return;
        }
        const ext = path.extname(filePath).toLowerCase();
        const mime = MIME[ext] || "application/octet-stream";
        res.writeHead(200, { "Content-Type": mime });
        res.end(data);
    });
});

// ============================================================
// SOCKET.IO
// ============================================================
const io = new Server(httpServer, {
    cors: { origin: "*", methods: ["GET", "POST"] }
});

/**
 * Структура комнаты:
 * activeRooms: Map<roomNumber, {
 *     winner: string|null,
 *     topClicks: string[],   // до 3 имён в порядке нажатия
 *     players: Map<playerName, { online: bool, score: number }>
 * }>
 */
const activeRooms = new Map();

// socket.id -> { roomNumber, playerName }
const socketRegistry = new Map();

// ============================================================
// ВСПОМОГАТЕЛЬНЫЕ ФУНКЦИИ
// ============================================================

/** Полный список игроков с очками и статусом — для админа. */
function buildScoresPayload(room) {
    const result = [];
    for (const [name, info] of room.players.entries()) {
        result.push({ name, score: info.score, online: info.online });
    }
    return result;
}

/** Рассылка админу и (опционально) игрокам снимка очков. */
function broadcastScores(roomNumber, room) {
    io.to(roomNumber).emit("onScoresUpdated", buildScoresPayload(room));
}

/** Рассылка обновлённого топа нажатий. */
function broadcastTopClicks(roomNumber, room) {
    io.to(roomNumber).emit("onTopClicksUpdated", room.topClicks);
}

/** Рассылка списка имён игроков (для совместимости со старой логикой). */
function broadcastPlayersList(roomNumber, room) {
    io.to(roomNumber).emit("onPlayersListUpdated", Array.from(room.players.keys()));
}

// ============================================================
// ПОДКЛЮЧЕНИЕ
// ============================================================
io.on("connection", (socket) => {
    console.log(`Подключился клиент: ${socket.id}`);

    // --------------------------------------------------------
    // [АДМИН] Создание комнаты
    // --------------------------------------------------------
    socket.on("createRoom", (roomNumber) => {
        if (!roomNumber) return;

        // Если такая комната уже есть — не пересоздаём, чтобы не сбросить состояние
        if (!activeRooms.has(roomNumber)) {
            activeRooms.set(roomNumber, {
                winner: null,
                topClicks: [],
                players: new Map()
            });
            console.log(`Администратор создал комнату: ${roomNumber}`);
        } else {
            console.log(`Комната ${roomNumber} уже существует — переиспользуем`);
        }

        socket.join(roomNumber);

        const room = activeRooms.get(roomNumber);
        // Сразу отправляем админу актуальные данные
        socket.emit("onPlayersListUpdated", Array.from(room.players.keys()));
        socket.emit("onScoresUpdated", buildScoresPayload(room));
        socket.emit("onTopClicksUpdated", room.topClicks);
    });

    // --------------------------------------------------------
    // [ИГРОК] Вход в комнату
    // --------------------------------------------------------
    socket.on("joinRoom", (roomNumber, playerName, callback) => {
        if (!activeRooms.has(roomNumber)) {
            if (typeof callback === "function") {
                callback({ success: false, message: "Ошибка: Такая комната еще не создана администратором!" });
            }
            return;
        }

        socket.join(roomNumber);
        const room = activeRooms.get(roomNumber);

        // Регистрируем сокет
        socketRegistry.set(socket.id, { roomNumber, playerName });

        if (room.players.has(playerName)) {
            console.log(`Игрок ${playerName} вернулся в комнату ${roomNumber}`);
            room.players.get(playerName).online = true;
        } else {
            console.log(`Новый игрок ${playerName} зашел в комнату ${roomNumber}`);
            room.players.set(playerName, { online: true, score: 0 });
        }

        if (typeof callback === "function") {
            callback({ success: true });
        }

        // Обновляем всех
        broadcastPlayersList(roomNumber, room);
        broadcastScores(roomNumber, room);

        // Если уже есть победитель — сообщаем только вернувшемуся
        if (room.winner) {
            socket.emit("onWinnerDetected", room.winner);
        }

        // Отправляем текущий счёт лично игроку, чтобы он видел свои очки
        const myInfo = room.players.get(playerName);
        socket.emit("onMyScore", { name: playerName, score: myInfo.score });
    });

    // --------------------------------------------------------
    // [ИГРОК] Нажатие кнопки
    // --------------------------------------------------------
    socket.on("playerClick", (data) => {
        const { roomNumber, playerName } = data || {};
        const room = activeRooms.get(roomNumber);
        if (!room) return;

        // Если первый ещё не зафиксирован — фиксируем
        if (room.winner === null) {
            room.winner = playerName;
            console.log(`В комнате ${roomNumber} первый: ${playerName}`);
            io.to(roomNumber).emit("onWinnerDetected", playerName);
        }

        // Обновляем топ-3 (без дубликатов, максимум 3)
        if (!room.topClicks.includes(playerName)) {
            room.topClicks.push(playerName);
            if (room.topClicks.length > 3) room.topClicks.length = 3;

            console.log(`ТОП нажатий в комнате ${roomNumber}: ${room.topClicks.join(", ")}`);
            broadcastTopClicks(roomNumber, room);
        }
    });

    // --------------------------------------------------------
    // [АДМИН] Изменение очков игрока
    // Ожидаем: { roomNumber, playerName, delta }
    // --------------------------------------------------------
    socket.on("updateScore", (data) => {
        const { roomNumber, playerName, delta } = data || {};
        const room = activeRooms.get(roomNumber);
        if (!room) return;

        if (!room.players.has(playerName)) {
            console.log(`updateScore: игрок ${playerName} не найден в комнате ${roomNumber}`);
            return;
        }

        const d = Number(delta);
        if (!Number.isFinite(d)) return;

        room.players.get(playerName).score += d;
        console.log(`Комната ${roomNumber}: ${playerName} ${d >= 0 ? "+" : ""}${d} (итог: ${room.players.get(playerName).score})`);

        broadcastScores(roomNumber, room);

        // Отправляем лично игроку его обновлённый счёт
        const info = room.players.get(playerName);
        io.to(roomNumber).emit("onMyScore", { name: playerName, score: info.score });
    });

    // --------------------------------------------------------
    // [АДМИН] Сброс раунда
    // --------------------------------------------------------
    socket.on("resetGame", (roomNumber) => {
        const room = activeRooms.get(roomNumber);
        if (!room) return;

        room.winner = null;
        room.topClicks = [];
        console.log(`Игра в комнате ${roomNumber} сброшена`);

        io.to(roomNumber).emit("onGameReset");
        broadcastTopClicks(roomNumber, room);
    });

    // --------------------------------------------------------
    // Отключение
    // --------------------------------------------------------
    socket.on("disconnect", () => {
        console.log(`Клиент отключился: ${socket.id}`);

        if (socketRegistry.has(socket.id)) {
            const { roomNumber, playerName } = socketRegistry.get(socket.id);
            socketRegistry.delete(socket.id);

            const room = activeRooms.get(roomNumber);
            if (room && room.players.has(playerName)) {
                room.players.get(playerName).online = false;
                console.log(`Игрок ${playerName} временно оффлайн (комната ${roomNumber})`);

                // Обновляем статус в таблице админа
                broadcastScores(roomNumber, room);
            }
        }
    });
});

// ============================================================
// СТАРТ
// ============================================================
const PORT = process.env.PORT || 5000;
httpServer.listen(PORT, () => {
    console.log(`===================================================`);
    console.log(` Сервер успешно запущен!`);
    console.log(` Локальный адрес: http://localhost:${PORT}`);
    console.log(`===================================================`);
});