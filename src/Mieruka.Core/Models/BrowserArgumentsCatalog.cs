using System;
using System.Collections.Generic;
using System.Linq;

namespace Mieruka.Core.Models;

/// <summary>
/// Static catalog of all known browser startup arguments grouped by category.
/// </summary>
public static class BrowserArgumentsCatalog
{
    private static readonly BrowserType[] Chromium = { BrowserType.Chrome, BrowserType.Edge, BrowserType.Brave };
    private static readonly BrowserType[] ChromeEdge = { BrowserType.Chrome, BrowserType.Edge };
    private static readonly BrowserType[] AllBrowsers = Array.Empty<BrowserType>();
    private static readonly BrowserType[] FirefoxOnly = { BrowserType.Firefox };
    private static readonly BrowserType[] EdgeOnly = { BrowserType.Edge };

    /// <summary>
    /// All available browser argument definitions.
    /// </summary>
    public static IReadOnlyList<BrowserArgumentDefinition> All { get; } = BuildCatalog();

    /// <summary>
    /// Returns arguments applicable to the given browser, optionally filtered by category.
    /// </summary>
    public static IReadOnlyList<BrowserArgumentDefinition> ForBrowser(BrowserType browser, BrowserArgumentCategory? category = null)
    {
        var query = All.Where(a => a.IsApplicableTo(browser));
        if (category.HasValue)
        {
            query = query.Where(a => a.Category == category.Value);
        }

        return query.ToList();
    }

    /// <summary>
    /// Returns all distinct categories that have at least one argument for the given browser.
    /// </summary>
    public static IReadOnlyList<BrowserArgumentCategory> CategoriesFor(BrowserType browser)
    {
        return All
            .Where(a => a.IsApplicableTo(browser))
            .Select(a => a.Category)
            .Distinct()
            .OrderBy(c => c)
            .ToList();
    }

    /// <summary>
    /// Tries to find a definition that matches the given flag string (with or without value).
    /// </summary>
    public static BrowserArgumentDefinition? FindByFlag(string argumentText)
    {
        if (string.IsNullOrWhiteSpace(argumentText))
        {
            return null;
        }

        // Strip value part for matching: "--proxy-server=host:port" → "--proxy-server"
        var flagPart = argumentText;
        var eqIndex = argumentText.IndexOf('=');
        if (eqIndex > 0)
        {
            flagPart = argumentText[..eqIndex];
        }

        return All.FirstOrDefault(a =>
            string.Equals(a.Flag, flagPart, StringComparison.OrdinalIgnoreCase));
    }

    private static List<BrowserArgumentDefinition> BuildCatalog()
    {
        return new List<BrowserArgumentDefinition>
        {
            // ── Display ───────────────────────────────────────────────
            new("--kiosk",
                "Modo Quiosque (Kiosk)",
                "Abre o navegador em tela cheia sem barras de ferramentas, endereço ou botões.",
                BrowserArgumentCategory.Display,
                Chromium),

            new("--start-fullscreen",
                "Tela Cheia",
                "Inicia o navegador em modo tela cheia (F11).",
                BrowserArgumentCategory.Display,
                Chromium),

            new("--start-maximized",
                "Maximizado",
                "Inicia o navegador com a janela maximizada.",
                BrowserArgumentCategory.Display,
                Chromium),

            new("--window-size",
                "Tamanho da Janela",
                "Define a largura e altura da janela do navegador (ex: 1920,1080).",
                BrowserArgumentCategory.Display,
                Chromium,
                RequiresValue: true,
                ValueHint: "largura,altura"),

            new("--window-position",
                "Posição da Janela",
                "Define a posição X,Y da janela (ex: 0,0).",
                BrowserArgumentCategory.Display,
                Chromium,
                RequiresValue: true,
                ValueHint: "x,y"),

            new("--force-device-scale-factor",
                "Escala de Exibição",
                "Força um fator de escala DPI específico (ex: 1.0, 1.5, 2.0).",
                BrowserArgumentCategory.Display,
                Chromium,
                RequiresValue: true,
                ValueHint: "1.0"),

            new("--kiosk",
                "Modo Quiosque Firefox",
                "Abre o Firefox em modo quiosque (tela cheia sem interface).",
                BrowserArgumentCategory.Display,
                FirefoxOnly),

            // ── Privacy ───────────────────────────────────────────────
            new("--incognito",
                "Modo Anônimo (Incognito)",
                "Abre o navegador em modo de navegação privada.",
                BrowserArgumentCategory.Privacy,
                new[] { BrowserType.Chrome, BrowserType.Brave }),

            new("--inprivate",
                "InPrivate (Edge)",
                "Abre o Edge em modo InPrivate.",
                BrowserArgumentCategory.Privacy,
                EdgeOnly),

            new("-private-window",
                "Navegação Privada (Firefox)",
                "Abre o Firefox em modo de navegação privada.",
                BrowserArgumentCategory.Privacy,
                FirefoxOnly),

            new("--guest",
                "Modo Convidado",
                "Inicia o navegador no perfil de convidado.",
                BrowserArgumentCategory.Privacy,
                Chromium),

            // ── Security ──────────────────────────────────────────────
            new("--disable-web-security",
                "Desabilitar Segurança Web",
                "Desativa verificações de CORS e segurança da web. ⚠ Usar apenas para testes.",
                BrowserArgumentCategory.Security,
                Chromium),

            new("--allow-running-insecure-content",
                "Permitir Conteúdo Inseguro",
                "Permite carregar conteúdo HTTP em páginas HTTPS.",
                BrowserArgumentCategory.Security,
                Chromium),

            new("--ignore-certificate-errors",
                "Ignorar Erros de Certificado",
                "Ignora erros de certificado SSL/TLS.",
                BrowserArgumentCategory.Security,
                Chromium),

            new("--disable-site-isolation-trials",
                "Desabilitar Isolamento de Sites",
                "Desativa o isolamento de processos por site.",
                BrowserArgumentCategory.Security,
                Chromium),

            // ── Performance ───────────────────────────────────────────
            new("--disable-gpu",
                "Desabilitar GPU",
                "Desativa a aceleração de hardware por GPU.",
                BrowserArgumentCategory.Performance,
                Chromium),

            new("--disable-software-rasterizer",
                "Desabilitar Rasterizador de Software",
                "Desativa o rasterizador de software do Chromium.",
                BrowserArgumentCategory.Performance,
                Chromium),

            new("--disable-dev-shm-usage",
                "Desabilitar /dev/shm",
                "Usa /tmp em vez de /dev/shm para memória compartilhada.",
                BrowserArgumentCategory.Performance,
                Chromium),

            new("--disable-background-timer-throttling",
                "Desabilitar Throttling em Background",
                "Impede que timers em abas em segundo plano sejam reduzidos.",
                BrowserArgumentCategory.Performance,
                Chromium),

            new("--disable-renderer-backgrounding",
                "Desabilitar Background do Renderer",
                "Impede que o renderizador seja colocado em segundo plano.",
                BrowserArgumentCategory.Performance,
                Chromium),

            new("--disable-backgrounding-occluded-windows",
                "Desabilitar Background de Janelas Ocultas",
                "Impede o throttling de janelas ocultas por outras.",
                BrowserArgumentCategory.Performance,
                Chromium),

            // ── Network ───────────────────────────────────────────────
            new("--proxy-server",
                "Servidor Proxy",
                "Define o servidor proxy a ser utilizado (ex: http://proxy:8080).",
                BrowserArgumentCategory.Network,
                Chromium,
                RequiresValue: true,
                ValueHint: "http://host:porta"),

            new("--proxy-bypass-list",
                "Lista de Bypass do Proxy",
                "Endereços que devem ignorar o proxy (ex: localhost;*.local).",
                BrowserArgumentCategory.Network,
                Chromium,
                RequiresValue: true,
                ValueHint: "localhost;*.local"),

            new("--no-proxy-server",
                "Sem Proxy",
                "Desativa qualquer proxy configurado no sistema.",
                BrowserArgumentCategory.Network,
                Chromium),

            new("--host-resolver-rules",
                "Regras de Resolução de Host",
                "Mapeia hosts para IPs específicos (ex: MAP * 127.0.0.1).",
                BrowserArgumentCategory.Network,
                Chromium,
                RequiresValue: true,
                ValueHint: "MAP host ip"),

            // ── Content ───────────────────────────────────────────────
            new("--no-first-run",
                "Pular Primeira Execução",
                "Ignora a tela de boas-vindas na primeira execução.",
                BrowserArgumentCategory.Content,
                Chromium),

            new("--disable-sync",
                "Desabilitar Sincronização",
                "Desativa a sincronização de conta Google/Microsoft.",
                BrowserArgumentCategory.Content,
                ChromeEdge),

            new("--disable-extensions",
                "Desabilitar Extensões",
                "Desativa todas as extensões instaladas no navegador.",
                BrowserArgumentCategory.Content,
                Chromium),

            new("--disable-features=Translate",
                "Desabilitar Tradução",
                "Desativa o recurso de tradução automática de páginas.",
                BrowserArgumentCategory.Content,
                Chromium),

            new("--disable-popup-blocking",
                "Desabilitar Bloqueio de Pop-ups",
                "Desativa o bloqueador de janelas pop-up.",
                BrowserArgumentCategory.Content,
                Chromium),

            new("--disable-infobars",
                "Desabilitar Barras de Informação",
                "Remove barras de informação automáticas do topo.",
                BrowserArgumentCategory.Content,
                Chromium),

            new("--disable-notifications",
                "Desabilitar Notificações",
                "Desativa todas as notificações do navegador.",
                BrowserArgumentCategory.Content,
                Chromium),

            new("--autoplay-policy=no-user-gesture-required",
                "Autoplay sem Interação",
                "Permite que vídeos toquem automaticamente sem interação do usuário.",
                BrowserArgumentCategory.Content,
                Chromium),

            new("--mute-audio",
                "Silenciar Áudio",
                "Silencia todo o áudio do navegador.",
                BrowserArgumentCategory.Content,
                Chromium),

            new("--disable-default-apps",
                "Desabilitar Apps Padrão",
                "Desativa a instalação de aplicativos web padrão.",
                BrowserArgumentCategory.Content,
                Chromium),

            // ── Debug ─────────────────────────────────────────────────
            new("--remote-debugging-port",
                "Porta de Depuração Remota",
                "Habilita depuração remota DevTools na porta especificada.",
                BrowserArgumentCategory.Debug,
                Chromium,
                RequiresValue: true,
                ValueHint: "9222"),

            new("--enable-logging",
                "Habilitar Logging",
                "Ativa logging interno do navegador.",
                BrowserArgumentCategory.Debug,
                Chromium),

            new("--v",
                "Nível de Verbosidade",
                "Define o nível de verbosidade do log (ex: 1).",
                BrowserArgumentCategory.Debug,
                Chromium,
                RequiresValue: true,
                ValueHint: "1"),
        };
    }
}
