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

function broadcastFinalAnswers(roomNumber, room) {
    const list = [];
    for (const [name] of room.players.entries()) {
        const fa = room.finalAnswers.get(name);
        list.push({
            name,
            bet: fa ? fa.bet : 0,
            answer: fa ? fa.answer : "",
            betSubmitted: fa ? !!fa.betSubmitted : false,
            answerSubmitted: fa ? !!fa.answerSubmitted : false
        });
    }
    io.to(roomNumber).emit("onFinalAnswersUpdated", list);
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
                players: new Map(),
                finalActive: false,
                finalPhase: null,           // "bets" | "answers"
                finalAnswers: new Map()     // playerName -> { bet, answer, betSubmitted, answerSubmitted }
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
        if (room.topClicks.length > 0) {
            socket.emit("onTopClicksUpdated", room.topClicks);
        }
        if (room.finalActive) {
            socket.emit("onFinalStarted");
            if (room.finalPhase === "answers") {
                socket.emit("onFinalAnswerPhase");
            }
        }
        broadcastFinalAnswers(roomNumber, room);
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

        // Топ-3 уже заполнен — нажатие игнорируем
        if (room.topClicks.length >= 3) return;

        // Дубликат — игрок уже в топе, повторный клик игнорируем
        if (room.topClicks.includes(playerName)) return;

        // Первый клик — фиксируем победителя (для админа и статуса)
        if (room.winner === null) {
            room.winner = playerName;
            console.log(`В комнате ${roomNumber} первый: ${playerName}`);
            io.to(roomNumber).emit("onWinnerDetected", playerName);
        }

        room.topClicks.push(playerName);
        console.log(`ТОП нажатий в комнате ${roomNumber}: ${room.topClicks.join(", ")}`);

        // Рассылаем обновление топ-3 всем в комнате
        broadcastTopClicks(roomNumber, room);
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
    // --------------------------------------------------------
    // [АДМИН] Начать финал — фаза ставок
    // --------------------------------------------------------
    socket.on("startFinal", (data) => {
        const { roomNumber } = data || {};
        const room = activeRooms.get(roomNumber);
        if (!room) return;

        room.finalActive = true;
        room.finalPhase = "bets";
        room.finalAnswers.clear();
        console.log(`Финал начат (фаза ставок) в комнате ${roomNumber}`);

        io.to(roomNumber).emit("onFinalStarted");
        broadcastFinalAnswers(roomNumber, room);
    });

    // --------------------------------------------------------
    // [ИГРОК] Отправить ставку
    // --------------------------------------------------------
    socket.on("submitFinalBet", (data) => {
        const { roomNumber, playerName, bet } = data || {};
        const room = activeRooms.get(roomNumber);
        if (!room || !room.finalActive || room.finalPhase !== "bets") return;

        const player = room.players.get(playerName);
        if (!player) return;

        const score = player.score || 0;
        if (score < 1) return;

        // Одна ставка на игрока
        const existing = room.finalAnswers.get(playerName);
        if (existing && existing.betSubmitted) return;

        let betNum = parseInt(bet, 10);
        if (!Number.isFinite(betNum)) return;
        if (betNum < 1) betNum = 1;
        if (betNum > score) betNum = score;

        room.finalAnswers.set(playerName, {
            bet: betNum,
            answer: existing ? existing.answer : "",
            betSubmitted: true,
            answerSubmitted: existing ? !!existing.answerSubmitted : false
        });

        console.log(`Финал [${roomNumber}] ${playerName}: ставка ${betNum}`);
        broadcastFinalAnswers(roomNumber, room);
    });

    // --------------------------------------------------------
    // [АДМИН] Открыть фазу ответов
    // --------------------------------------------------------
    socket.on("openFinalAnswers", (data) => {
        const { roomNumber } = data || {};
        const room = activeRooms.get(roomNumber);
        if (!room || !room.finalActive) return;

        room.finalPhase = "answers";

        // Всем, кто не сделал ставку — по умолчанию 1.
        // При этом betSubmitted = true, чтобы игрок мог отправить ответ.
        for (const [name] of room.players.entries()) {
            const existing = room.finalAnswers.get(name);
            if (!existing || !existing.betSubmitted) {
                room.finalAnswers.set(name, {
                    bet: 1,
                    answer: existing ? existing.answer : "",
                    betSubmitted: true,
                    answerSubmitted: false
                });
                console.log(`Финал [${roomNumber}] ${name}: ставка по умолчанию 0`);
            }
        }

        console.log(`Финал: фаза ответов в комнате ${roomNumber}`);

        io.to(roomNumber).emit("onFinalAnswerPhase");
        broadcastFinalAnswers(roomNumber, room);
    });

    // --------------------------------------------------------
    // [ИГРОК] Отправить ответ
    // --------------------------------------------------------
    socket.on("submitFinalAnswer", (data) => {
        const { roomNumber, playerName, answer } = data || {};
        const room = activeRooms.get(roomNumber);
        if (!room || !room.finalActive || room.finalPhase !== "answers") return;

        const player = room.players.get(playerName);
        if (!player) return;

        const entry = room.finalAnswers.get(playerName);
        if (!entry || !entry.betSubmitted) return;      // ставка не сделана
        if (entry.answerSubmitted) return;               // уже отправлено

        const answerStr = String(answer || "").trim();
        if (!answerStr) return;

        entry.answer = answerStr;
        entry.answerSubmitted = true;

        console.log(`Финал [${roomNumber}] ${playerName}: ответ "${answerStr}"`);
        broadcastFinalAnswers(roomNumber, room);
    });

    // --------------------------------------------------------
    // [АДМИН] Завершить финал
    // --------------------------------------------------------
    socket.on("endFinal", (data) => {
        const { roomNumber } = data || {};
        const room = activeRooms.get(roomNumber);
        if (!room) return;

        room.finalActive = false;
        room.finalPhase = null;
        room.finalAnswers.clear();
        console.log(`Финал завершён в комнате ${roomNumber}`);

        io.to(roomNumber).emit("onFinalEnded");
        broadcastFinalAnswers(roomNumber, room);
    });

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