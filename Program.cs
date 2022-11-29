using System.CommandLine;
using System.CommandLine.Invocation;
using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Edge;
using OpenQA.Selenium.Firefox;
using OpenQA.Selenium.Support.UI;

RootCommand rootCommand = new();

Option<string> browserOption = new(new[] { "--browser", "-b" }, "Which browser to use");
browserOption.AddCompletions("chrome", "chromium", "firefox", "edge");

if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) browserOption.SetDefaultValue("edge");
else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) browserOption.SetDefaultValue("firefox");

browserOption.AddValidator(result =>
{
    if (result.Tokens.Count == 1 && result.Tokens[0].Value is { } browser &&
        new[] { "chrome", "chromium", "firefox", "edge" }.Contains(browser.ToLower())) return;
    Console.WriteLine("Browser must be either chrome, chromium, firefox or edge");
    Environment.Exit(1);
});
browserOption.IsRequired = true;

Option<string[]> urlsOption = new(new[]
    {
        "--urls",
        "-u"
    },
    "The URL/s to open, you can do do for example -u example.com example-1.com\n" +
    "Or -u example.com -u example-1.com");
urlsOption.AddValidator(result =>
{
    result.Tokens.Select(t => t.Value)
        .Where(url => url is not { } || 
                      !Uri.IsWellFormedUriString(url, UriKind.Absolute))
        .ToList()
        .ForEach(url => Console.WriteLine($"URL {url} is not valid"));
});
urlsOption.IsRequired = false;
urlsOption.AllowMultipleArgumentsPerToken = true;

Option<string> urlsPathOption = new(new[]
    {
        "--urls-path",
        "-up"
    },
    "The path to a file containing URLs to open\nEach URL must be place on a new line");
urlsPathOption.AddValidator(result =>
{
    foreach (var path in result.Tokens.Select(t => t.Value))
    {
        if (path is not { })
        {
            Console.WriteLine("Path cannot be empty");
            continue;
        }

        if (!File.Exists(path))
        {
            Console.WriteLine($"File {path} does not exist");
            continue;
        }
        
        if (!File.ReadAllLines(path).Any(url => url is not { } ||
                                                !Uri.IsWellFormedUriString(url, UriKind.Absolute))) continue;
        // get invalid url and tell user to remove that url from list
        var invalidUrl = File.ReadAllLines(path).First(url => url is not { } ||
                                                             !Uri.IsWellFormedUriString(url, UriKind.Absolute));
        Console.WriteLine($"URL: \"{invalidUrl}\" in file {path} is not valid\nPlease remove it from the file");
        Environment.Exit(1);
    }
});
urlsPathOption.IsRequired = false;

Option<string> loginFileOption = new(new[] { "--login-file", "-l" }, "The file containing the login credentials");
loginFileOption.AddValidator(result =>
{
    var extension = Path.GetExtension(result.Tokens[0].Value);
    if (extension is not { } || !new[] { ".txt", ".json" }.Contains(extension.ToLower()))
        Console.WriteLine("Login file must be either a .txt or .json file");
    else switch (extension)
    {
        case ".txt":
        {
            try
            {
                var lines = File.ReadAllLines(result.Tokens[0].Value);
                if (lines.Length != 2 || lines[0] is not { } || lines[1] is not { })
                    Console.WriteLine(
                        "Login file must contain an email on the first line and a password on the second line");
                if (lines[0] is not { } || !new EmailAddressAttribute().IsValid(lines[0]))
                {
                    Console.WriteLine("Email is not valid");
                    Environment.Exit(1);
                }
            }
            catch (FileNotFoundException e)
            { 
                Console.WriteLine($"File {e.FileName} does not exist");
                Environment.Exit(1);
            }
            break;
        }
        case ".json":
        {
            string? json = null;
            try
            {
                json = File.ReadAllText(result.Tokens[0].Value);
            }
            catch (FileNotFoundException e)
            { 
                Console.WriteLine($"File {e.FileName} does not exist");
                Environment.Exit(1);
            }
            JsonSerializer.Deserialize<Login>(json);
            if (json is not { } || !new EmailAddressAttribute().IsValid(JsonSerializer.Deserialize<Login>(json)?.Email!))
            {
                Console.WriteLine("Email is not valid");
                Environment.Exit(1);
            }
            break;
        }
    }
});
loginFileOption.IsRequired = true;

foreach (var option in new Option[]
         {
             browserOption,
             urlsOption,
             urlsPathOption,
             loginFileOption
         })
    rootCommand.AddOption(option);

rootCommand.SetHandler(HandleInput);

await rootCommand.InvokeAsync(args);

Task HandleInput(InvocationContext invocationContext)
{
    var webDriver = invocationContext.ParseResult.GetValueForOption(browserOption);

    WebDriver? driver = null;
    try
    {
        driver = webDriver switch
        {
            "firefox" => new FirefoxDriver(),
            "chrome" => new ChromeDriver(),
            "chromium" => new ChromeDriver(),
            "edge" => new EdgeDriver(),
            _ => throw new Exception("Invalid browser")
        };
    }
    catch (DriverServiceNotFoundException e)
    {
        if (e.Message.Contains("driver"))
        {
            Console.WriteLine($"Browser {webDriver} is not installed");
            Environment.Exit(1);
        }
    }

    var urls = invocationContext.ParseResult.GetValueForOption(urlsOption);
    var paths = invocationContext.ParseResult.GetValueForOption(urlsPathOption);
    urls = paths is not { } ? urls : File.ReadAllLines(paths);
    
    // if urls is null or empty throw an error
    if (urls is not { }) 
        throw new InvalidOperationException("No URLs provided");

    var loginFile = invocationContext.ParseResult.GetValueForOption(loginFileOption);
    var extension = new FileInfo(loginFile!).Extension;
    
    (string email, string password) GetLoginFromTxt(string s)
    {
        var lines = File.ReadAllLines(s);
        return (lines[0], lines[1]);
    }

    (string email, string password) GetLoginFromJson(string s)
    {
        var json = File.ReadAllText(s);
        var login = JsonSerializer.Deserialize<Login>(json);
        return (login?.Email, login?.Password)!;
    }

    var (email, password) = extension switch
    {
        ".txt" => GetLoginFromTxt(loginFile ?? string.Empty),
        ".json" => GetLoginFromJson(loginFile ?? string.Empty),
        _ => throw new InvalidOperationException("Invalid login file")
    };

    if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
    {
        Console.WriteLine("Your missing an email or a password");
        return Task.CompletedTask;
    }

    driver?.Navigate().GoToUrl("https://archive.org/account/login");
    driver?.FindElement(By.Name("username")).SendKeys(email);
    driver?.FindElement(By.Name("password")).SendKeys(password);
    driver?.FindElement(By.Name("submit-to-login")).Click();
    
    SaveUrl(driver, urls);
    driver?.Quit();
    return Task.CompletedTask;
}

void SaveUrl(IWebDriver? webDriver, IEnumerable<string>? urls)
{
    var enumerable = (urls ?? Array.Empty<string>()).ToList();
    foreach (var url in enumerable)
    {
        Thread.Sleep(3500);
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
            if(e.Data.Contains("timed out"))
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

        WebDriverWait? wait = null;
        try
        {
            var savingMsg = webDriver?.FindElement(By.Id("saving-msg"));
            if ((bool)savingMsg?.Displayed)
                Console.WriteLine($"Saving URL: {url}");
            wait = new WebDriverWait(webDriver, TimeSpan.FromSeconds(60));
            wait.Until(_ => !savingMsg.Displayed);
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
                    Console.WriteLine($"{url} is saved");
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
                Console.WriteLine($"URL: {url} has already been captured 10 times");
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
                Console.WriteLine($"Already existing snapshot: {url}");
                continue;
            }
        }
        catch (WebDriverException) { /* ignored */ }
        
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"Saved: {url}");
        Console.ResetColor();
    }
}

public class Login
{
    public Login(string email, string password)
    {
        Email = email;
        Password = password;
    }

    [JsonPropertyName("email")]
    public string Email { get; set; }

    [JsonPropertyName("password")]
    public string Password{ get; set; }
}