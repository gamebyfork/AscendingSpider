using System.Collections.Generic;

namespace AscendingSpider;

public static class Localization
{
    public static string Language = "EN";

    private static readonly Dictionary<string, (string EN, string RU, string ES, string ZH, string JA)> _strings = new()
    {
        { "Title", ("Ascending Spider", "Восходящий Паук", "Araña Ascendente", "上升蜘蛛纸牌", "アセンディング・スパイダー") },
        { "NewGame", ("New Game", "Новая игра", "Nuevo Juego", "新游戏", "新しいゲーム") },
        { "Continue", ("Continue", "Продолжить", "Continuar", "继续", "続ける") },
        { "Settings", ("Settings", "Настройки", "Ajustes", "设置", "設定") },
        { "Achievements", ("Achievements", "Достижения", "Logros", "成就", "実績") },
        { "Exit", ("Exit", "Выход", "Salir", "退出", "終了") },
        { "Suits", ("Suits", "Количество мастей", "Palos", "花色数量", "スート数") },
        { "Difficulty", ("Difficulty", "Сложность", "Dificultad", "难度", "難易度") },
        { "Ruleset", ("Rules", "Правила", "Reglas", "规则", "ルール") },
        { "ClassicRules", ("Classic", "Классические", "Clásicas", "经典规则", "クラシック") },
        { "RelaxedRules", ("Relaxed", "Облегчённые", "Relajadas", "简化规则", "やさしいルール") },
        { "ClassicRulesDesc", ("Only same-suit descending runs can move together.", "Вместе перемещаются только последовательности одной масти.", "Solo se pueden mover juntas secuencias descendentes del mismo palo.", "只有同花色的连续降序牌组才能一起移动。", "同じスートの降順シーケンスだけをまとめて移動できます。") },
        { "RelaxedRulesDesc", ("Any descending run can move together; collection still requires one suit.", "Можно перемещать нисходящие последовательности любых мастей; для сбора по-прежнему нужна одна масть.", "Se puede mover cualquier secuencia descendente; para recogerla aún se necesita un solo palo.", "任何降序牌组都可以一起移动；收牌仍必须是同一花色。", "スートが混在した降順シーケンスも移動できますが、回収には同じスートが必要です。") },
        { "Easy", ("Easy", "Лёгкая", "Fácil", "简单", "簡単") },
        { "Medium", ("Medium", "Средняя", "Medio", "中等", "普通") },
        { "Hard", ("Hard", "Сложная", "Difícil", "困难", "難しい") },
        { "Language", ("Language", "Язык", "Idioma", "语言", "言語") },
        { "Volume", ("Volume", "Громкость", "Volumen", "音量", "音量") },
        { "TableStyle", ("Table Style", "Стиль стола", "Estilo de Mesa", "桌面风格", "テーブルスタイル") },
        { "CardBack", ("Card Back", "Рубашка карт", "Dorso de Carta", "牌背样式", "カードの裏面") },
        { "Classic", ("Classic", "Классический", "Clásico", "经典", "クラシック") },
        { "Dark", ("Dark", "Тёмный", "Oscuro", "暗黑", "ダーク") },
        { "Ocean", ("Ocean", "Океан", "Océano", "海洋", "オーシャン") },
        { "Burgundy", ("Burgundy", "Бордовый", "Burdeos", "酒红", "バーガンディ") },
        { "Sand", ("Sand", "Песок", "Arena", "沙色", "サンド") },
        { "Red", ("Red", "Красный", "Rojo", "红色", "赤") },
        { "Pattern", ("Pattern", "Узор", "Patrón", "图案", "模様") },
        { "Minimalism", ("Minimalism", "Минимализм", "Minimalismo", "极简", "ミニマル") },
        { "Space", ("Space", "Космос", "Espacio", "太空", "スペース") },
        { "Golden", ("Golden", "Золотая", "Dorada", "金色", "ゴールド") },
        { "Back", ("Back", "Назад", "Volver", "返回", "戻る") },
        { "Start", ("Start", "Начать", "Empezar", "开始", "スタート") },
        { "Menu", ("Menu", "Меню", "Menú", "菜单", "メニュー") },
        { "Win", ("You Win!", "Вы победили!", "¡Has Ganado!", "你赢了！", "勝利！") },
        { "Stuck", ("No More Moves!", "Ходов больше нет!", "¡Sin Movimientos!", "无路可走！", "手詰まり！") },
        { "Undo", ("Undo", "Отменить ход", "Deshacer", "撤销", "元に戻す") },
        { "Hint", ("Hint", "Подсказка", "Pista", "提示", "ヒント") },
        { "Restart", ("Restart", "Заново", "Reiniciar", "重新开始", "やり直す") },
        { "1Suit", ("1 Suit", "1 масть", "1 Palo", "1种花色", "1スート") },
        { "2Suits", ("2 Suits", "2 масти", "2 Palos", "2种花色", "2スート") },
        { "3Suits", ("3 Suits", "3 масти", "3 Palos", "3种花色", "3スート") },
        { "4Suits", ("4 Suits", "4 масти", "4 Palos", "4种花色", "4スート") },
        { "Deals", ("Deals", "Раздач", "Repartos", "发牌", "配布") },
        { "Time", ("Time", "Время", "Tiempo", "时间", "時間") },
        { "BestTime", ("Best", "Лучшее", "Mejor", "最佳", "ベスト") },
        { "Wins", ("Wins", "Победы", "Victorias", "胜利", "勝利数") },
        { "Generating", ("Generating deal...", "Генерация раздачи...", "Generando reparto...", "生成牌局...", "配布を生成中...") },
        { "NoVerifiedDeal", ("No solvable deal was proven in time. Retry or choose an easier difficulty.", "Не удалось доказать решаемость раздачи за отведённое время. Попробуйте ещё раз или выберите более лёгкую сложность.", "No se pudo demostrar que el reparto sea resoluble a tiempo. Reintenta o elige una dificultad menor.", "未能在限定时间内证明牌局可解。请重试或选择较低难度。", "制限時間内に解決可能な配布を証明できませんでした。再試行するか難易度を下げてください。") },
        { "HintSearching", ("Analyzing the position...", "Анализируем позицию...", "Analizando la posición...", "正在分析局面...", "局面を分析中...") },
        { "HintUnavailable", ("No reliable hint was found within the search limit.", "В пределах лимита поиска не удалось найти надёжную подсказку.", "No se encontró una pista fiable dentro del límite de búsqueda.", "在搜索限制内未找到可靠提示。", "探索上限内に信頼できるヒントが見つかりませんでした。") },
        { "Attempt", ("Attempt", "Попытка", "Intento", "尝试", "試行") },
        { "FallbackDeal", ("Solvability not verified (search limit reached).", "Решаемость не подтверждена: достигнут лимит поиска.", "No se pudo verificar la solución: se alcanzó el límite de búsqueda.", "未能验证此牌局是否可解：已达到搜索上限。", "探索上限に達したため、この配布の解決可能性は未確認です。") },
        { "DealRejected", ("Proven impossible and rejected", "Доказано, что решений нет", "Repartos imposibles descartados", "已证明无解并已排除", "解決不能と判定して除外") },
        { "DealUnverified", ("Search limit reached", "Поиск не завершён", "Límite de búsqueda alcanzado", "已达到搜索上限", "探索上限に到達") },
        { "ResetStats", ("Reset Stats", "Сбросить статистику", "Reiniciar Estadísticas", "重置统计", "統計をリセット") },
        { "ConfirmReset", ("Are you sure?", "Вы уверены?", "¿Estás seguro?", "你确定吗？", "本当によろしいですか？") },
        { "TutorialTitle", ("How to Play", "Как играть", "Cómo Jugar", "玩法说明", "遊び方") },
        { "TutorialText", ("Drag cards to move them. You can stack any card on a card one rank higher. Suit matters only for collecting.\n\nCollect A—K sequences of the same suit.\n\nZ = Undo, H = Hint, Esc = Menu", 
            "Перетаскивайте карты мышью. На любую карту можно положить карту на ранг ниже (масть важна только для сбора последовательностей).\n\nСобирайте последовательности A—K одной масти.\n\nZ — отмена хода, H — подсказка, Esc — меню", 
            "Arrastra cartas para moverlas. Puedes apilar cualquier carta sobre una de un rango mayor (el palo solo importa al recoger).\n\nRecoge secuencias A—K del mismo palo.\n\nZ = Deshacer, H = Pista, Esc = Menú", 
            "拖动卡片进行移动。任何卡片都可以堆叠在比它大一级的卡片上（花色只影响收牌）。\n\n收集同花色的 A—K 序列。\n\nZ = 撤销，H = 提示，Esc = 菜单", 
            "カードをドラッグして移動します。1つ上のランクのカードに重ねられます（スートは回収時のみ重要）。\n\n同じスートのA—Kのシーケンスを集めます。\n\nZ = 元に戻す、H = ヒント、Esc = メニュー") },
        { "TutorialClick", ("Click to continue", "Нажмите, чтобы продолжить", "Haz clic para continuar", "点击继续", "クリックして続ける") },
        { "AchFirstWin", ("First Victory", "Первая победа", "Primera Victoria", "首次胜利", "初勝利") },
        { "AchFirstWinDesc", ("Win a game", "Выиграйте партию", "Gana una partida", "赢得一场比赛", "ゲームに勝利") },
        { "AchNoUndo", ("Without Regret", "Без сожалений", "Sin Arrepentimiento", "无悔", "後悔なし") },
        { "AchNoUndoDesc", ("Win without using Undo", "Выиграйте без отмены хода", "Gana sin deshacer", "不使用撤销获胜", "元に戻さず勝利") },
        { "AchNoHint", ("Self-Reliant", "Своим умом", "Autosuficiente", "独立自主", "自立") },
        { "AchNoHintDesc", ("Win without using Hint", "Выиграйте без подсказок", "Gana sin pistas", "不使用提示获胜", "ヒントを使わず勝利") },
        { "AchSpeedrun", ("Speedrunner", "Скороход", "Velocista", "速通", "スピードランナー") },
        { "AchSpeedrunDesc", ("Win in under 5 minutes", "Выиграйте за 5 минут", "Gana en menos de 5 minutos", "5分钟内获胜", "5分以内に勝利") },
        { "Ach4Suits", ("Two-Suit Master", "Мастер двух мастей", "Maestro de dos palos", "双花色大师", "2スートマスター") },
        { "Ach4SuitsDesc", ("Win with 2 suits", "Выиграйте на 2 мастях", "Gana con 2 palos", "使用2种花色获胜", "2スートで勝利") },
        { "AchHardMode", ("Hardcore", "Хардкор", "Modo Difícil", "硬核", "ハードコア") },
        { "AchHardModeDesc", ("Win on hard difficulty", "Выиграйте на сложном уровне", "Gana en difícil", "困难难度获胜", "難しい難易度で勝利") },
        { "AchDedicated", ("Passion", "Азарт", "Pasión", "热情", "情熱") },
        { "AchDedicatedDesc", ("Win 10 games", "Выиграйте 10 партий", "Gana 10 partidas", "赢得10场比赛", "10勝する") },
        { "AchPerfectOrder", ("Perfect Order", "Идеальный порядок", "Orden Perfecto", "完美秩序", "パーフェクトオーダー") },
        { "AchPerfectOrderDesc", (
            "Win a game without ever placing a card of one suit onto a card of another suit.",
            "Выиграйте партию, ни разу не положив карту одной масти на карту другой масти.",
            "Gana una partida sin colocar nunca una carta de un palo sobre una carta de otro palo.",
            "赢得一场游戏，且从未将一种花色的牌放在另一种花色的牌上。",
            "異なるスートのカードの上に別のスートのカードを一度も置かずに勝利する") },
        { "AchUnlocked", ("Unlocked!", "Получено!", "¡Desbloqueado!", "已解锁！", "解除！") },
        { "Resume", ("Resume", "Продолжить", "Reanudar", "继续", "再開") },
        { "Paused", ("Paused", "Пауза", "Pausa", "暂停", "一時停止") },
        { "MainMenu", ("Main Menu", "Главное меню", "Menú Principal", "主菜单", "メインメニュー") },
        { "Mode", ("Mode", "Режим", "Modo", "模式", "モード") },
        { "Standard", ("Standard", "Стандартный", "Estándar", "标准", "スタンダード") },
        { "Blitz", ("Blitz", "Блиц", "Blitz", "闪电战", "ブリッツ") },
        { "Endless", ("Endless", "Бесконечный", "Infinito", "无尽", "エンドレス") },
        { "Loops", ("Loops", "Круги", "Rondas", "轮数", "ループ") },
        { "TimeUp", ("Time's up!", "Время вышло!", "¡Tiempo agotado!", "时间到！", "時間切れ！") },
        { "Leaderboard", ("Leaderboard", "Таблица лидеров", "Marcadores", "排行榜", "ランキング") },
        { "NoScores", ("No records yet", "Пока нет рекордов", "Sin récords aún", "暂无记录", "記録なし") }
    };

    public static string Get(string key)
    {
        if (_strings.TryGetValue(key, out var val))
        {
            return Language switch
            {
                "EN" => val.EN,
                "ES" => val.ES,
                "ZH" => val.ZH,
                "JA" => val.JA,
                _ => val.RU
            };
        }
        return key;
    }
}