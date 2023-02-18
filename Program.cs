using System.CommandLine;
using System.CommandLine.Invocation;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Edge;
using OpenQA.Selenium.Firefox;
using OpenQA.Selenium.Support.UI;
using Pastel;

RootCommand rootCommand = new();

Option<string> browserOption = new(new[] { "--browser", "-b" }, "Which browser to use"){ IsRequired = true};
Enum.GetNames(typeof(WebDriverType)).ToList().ForEach(browser => browserOption.AddCompletions(browser));

browserOption.AddValidator(result =>
{
    var value = result.GetValueOrDefault<string>();
    if (value is null)
    {
        Console.Error.WriteLine("Browser cannot be empty".Pastel(ConsoleColor.Red));
        Environment.Exit(1);
    }
    if (Enum.TryParse<WebDriverType>(value, true, out _))
        return;

    Console.Error.WriteLine("Browser must be either chrome, chromium, firefox or edge");
    Environment.Exit(1);
});

Option<string[]> urlsOption = new(new[] { "--urls", "-u" },
    "The URL/s to open, you can do do for example -u youtube.com twitter.com\n" +
    "Or -u youtube.com -u twitter.com") { AllowMultipleArgumentsPerToken = true };
urlsOption.AddValidator(result =>
{
    var value = result.GetValueOrDefault<string[]>();
    if (value is null)
    {
        Console.Error.WriteLine("URL cannot be empty".Pastel(ConsoleColor.Red));
        Environment.Exit(1);
    }
    foreach (var url in value)
    {
        if (Uri.IsWellFormedUriString(url, UriKind.Absolute))
            continue;
        Console.WriteLine($"URL: \"{url}\" is not valid, and it will be ignored");
    }
});

Option<string[]> urlsPathOption = new(new[] { "--urls-path", "-up" },
    "The path to a file containing URLs to open\nEach URL must be place on a new line");
urlsPathOption.AddValidator(result =>
{
    var paths = result.GetValueOrDefault<string[]>();
    if (paths is null)
    {
        Console.Error.WriteLine("Path cannot be empty".Pastel(ConsoleColor.Red));
        Environment.Exit(1);
    }
    foreach (var path in paths)
    {
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"File {path} does not exist".Pastel(ConsoleColor.Red));
            continue;
        }

        var lines = File.ReadAllLines(path);
        foreach (var line in lines)
        {
            if (!Uri.TryCreate(line, UriKind.RelativeOrAbsolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                Console.WriteLine($"URL: \"{line}\" is not valid, and it will be ignored".Pastel(ConsoleColor.DarkYellow));
            }
        }
    }
});

Option<string> loginFileOption = new(new[] { "--login-file", "-l" }, "The JSON file containing the login credentials") { IsRequired = true};
loginFileOption.AddValidator(result =>
{
    var value = result.GetValueOrDefault<string>();
    if (value is null)
    {
        Console.Error.WriteLine("Login file cannot be empty".Pastel(ConsoleColor.Red));
        Environment.Exit(1);
    }
    if (!File.Exists(value))
    {
        Console.Error.WriteLine($"File {value} does not exist".Pastel(ConsoleColor.Red));
        Environment.Exit(1);
    }

    var json = File.ReadAllText(value);
    LoginJson? deserialize = null;
    try
    {
        deserialize = JsonSerializer.Deserialize<LoginJson>(json);
    }
    catch (Exception)
    {
        Console.Error.WriteLine("Error while parsing login file".Pastel(ConsoleColor.Red));
        Environment.Exit(1);
    }
    var validEmail = new EmailAddressAttribute().IsValid(deserialize!.Email);
    if (validEmail)
        return;

    Console.Error.WriteLine("Email is not valid".Pastel(ConsoleColor.Red));
    Environment.Exit(1);
});

Option[] options = { browserOption, urlsOption, urlsPathOption, loginFileOption };
foreach (var option in options)
    rootCommand.AddOption(option);

rootCommand.SetHandler(HandleInput);

await rootCommand.InvokeAsync(args);

IWebDriver GetWebDriver(WebDriverType driverType)
{
    switch (driverType)
    {
        case WebDriverType.Chrome:
            {
                var service = ChromeDriverService.CreateDefaultService();
                service.EnableVerboseLogging = false;
                return new ChromeDriver(service);
            }
        case WebDriverType.Firefox:
            {
                var service = FirefoxDriverService.CreateDefaultService();
                service.LogLevel = FirefoxDriverLogLevel.Fatal;
                return new FirefoxDriver(service);
            }
        case WebDriverType.Edge:
            {
                var service = EdgeDriverService.CreateDefaultService();
                service.UseVerboseLogging = false;
                return new EdgeDriver(service);
            }
        default:
            throw new ArgumentException("Invalid WebDriverType specified");
    }
}

Task HandleInput(InvocationContext invocationContext)
{
    var webDriver = invocationContext.ParseResult.GetValueForOption(browserOption)!;

    IWebDriver driver;
    try
    {
        driver = GetWebDriver(Enum.Parse<WebDriverType>(webDriver, true));
    }
    catch (Exception)
    {
        Console.Error.WriteLine("Error while creating WebDriver".Pastel(ConsoleColor.Red));
        Environment.Exit(1);
        return Task.CompletedTask;
    }

    var urls = invocationContext.ParseResult.GetValueForOption(urlsOption);
    // gets all urls that are valid and ignores the ones that are not
    urls = urls?.Where(url => Uri.IsWellFormedUriString(url, UriKind.Absolute)).ToArray();
    if (urls is null)
    {
        Console.Error.WriteLine("URL cannot be empty".Pastel(ConsoleColor.Red));
        Environment.Exit(1);
    }
    // If path is not null it will read the file and add the URLs to the array
    var paths = invocationContext.ParseResult.GetValueForOption(urlsPathOption);
    if (paths is not null)
    {
        foreach (var path in paths)
        {
            if (!File.Exists(path))
            {
                Console.Error.WriteLine($"File {path} does not exist".Pastel(ConsoleColor.Red));
                continue;
            }

            var lines = File.ReadAllLines(path);
            urls = urls.Concat(lines.Where(url => Uri.IsWellFormedUriString(url, UriKind.Absolute))).ToArray();
        }
    }

    var loginFile = invocationContext.ParseResult.GetValueForOption(loginFileOption);

    var deserialize = JsonSerializer.Deserialize<LoginJson>(File.ReadAllText(loginFile!));
    var email = deserialize!.Email;
    var password = deserialize.Password;

    driver.Navigate().GoToUrl("https://archive.org/account/login");
    driver.FindElement(By.Name("username")).SendKeys(email);
    driver.FindElement(By.Name("password")).SendKeys(password);
    driver.FindElement(By.Name("submit-to-login")).Click();
    Thread.Sleep(TimeSpan.FromSeconds(3));

    IWebElement? error = null;
    try
    {
        error = driver.FindElement(By.CssSelector("span.login-error"));
    }
    catch (Exception) { /* ignored */ }
    if (error is not null)
    {
        Console.Error.WriteLine("Invalid Email or Password".Pastel(ConsoleColor.Red));
        driver.Quit();
        return Task.CompletedTask;
    }

    SaveUrl(driver, urls);
    driver.Quit();
    return Task.CompletedTask;
}

void SaveUrl(IWebDriver? webDriver, IEnumerable<string> urls)
{
    foreach (var url in urls)
    {
        Thread.Sleep(TimeSpan.FromSeconds(5));
        try
        {
            webDriver?.Navigate().GoToUrl("https://web.archive.org/save");
        }
        catch (WebDriverTimeoutException)
        {
            Console.WriteLine("Wayback Machine has timed out please try again");
        }
        catch (WebDriverException e)
        {
            if (e.Data.Contains("timed out"))
                Console.WriteLine("Wayback Machine has timed out please try again");
        }

        var inputUrl = webDriver?.FindElement(By.Id("web-save-url-input"));
        inputUrl?.SendKeys(url);

        var captureOutlinks = webDriver?.FindElement(By.Id("capture_outlinks"));
        captureOutlinks?.Click();

        var captureScreenshot = webDriver?.FindElement(By.Id("capture_screenshot"));
        captureScreenshot?.Click();

        var savePageButton = webDriver?.FindElement(By.ClassName("web-save-button"));
        savePageButton?.Click();

        Stopwatch? stopWatch = null;
        WebDriverWait? wait = null;
        try
        {
            var savingMsg = webDriver?.FindElement(By.Id("saving-msg"));
            stopWatch = new Stopwatch();
            if ((bool)savingMsg?.Displayed)
            {
                stopWatch.Start();
                Console.WriteLine($"Saving URL: {url}");
            }
            wait = new WebDriverWait(webDriver, TimeSpan.FromSeconds(60));
            wait.Until(_ => !savingMsg.Displayed);
            stopWatch.Stop();
        }
        catch (NoSuchElementException) { /* ignored */ }

        if (wait is not null)
        {
            try
            {
                var doneMsg =
                    webDriver?.FindElement(By.CssSelector("span.label-success:nth-child(2) > span:nth-child(1)"));
                if (doneMsg is { Displayed: true })
                {
                    Console.WriteLine($"Saved: {url} in {stopWatch?.Elapsed.TotalSeconds:#.##} seconds".Pastel(ConsoleColor.Green));
                    continue;
                }
            }
            catch (NoSuchElementException) { /* ignored */ }
        }

        try
        {
            var error = webDriver?.FindElement(By.CssSelector(".col-md-offset-4 > p:nth-child(2)"));
            if (error!.Displayed)
            {
                Console.WriteLine($"URL: {url} has already been captured 10 times".Pastel(ConsoleColor.DarkYellow));
                continue;
            }
        }
        catch (NoSuchElementException) { /* ignored */ }

        try
        {
            var captureMsg = webDriver?.FindElement(By.CssSelector(".col-md-8 > p:nth-child(2)"));
            if ((bool)captureMsg?.Displayed)
                Console.WriteLine($"URL: {url} is being captured");
            continue;
        }
        catch (NoSuchElementException) { /* ignored */ }

        try
        {
            var snapshot = webDriver?.FindElements(
                By.XPath("//p[contains(text(), 'The same snapshot had been made')]"));
            if ((bool)snapshot?.Any())
            {
                Console.WriteLine($"Already existing snapshot: {url}".Pastel(ConsoleColor.DarkYellow));
            }
        }
        catch (WebDriverException) { /* ignored */ }
    }
}

internal enum WebDriverType
{
    Chrome,
    Firefox,
    Edge
}

internal class LoginJson
{
    public LoginJson(string email, string password)
    {
        Email = email;
        Password = password;
    }

    [JsonPropertyName("email")]
    public string Email { get; set; }

    [JsonPropertyName("password")]
    public string Password { get; set; }
}