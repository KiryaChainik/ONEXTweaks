using System;
using System.Collections.Generic;
using System.Reflection;
namespace ONEXTweaks.Calibration
{
 internal static class CalibratorText
 {
  private static string language="en";
  internal static string Language {get{return language;}}
  internal static string LanguageSource {get;private set;}
  internal static string ReadErrorType {get;private set;}
  internal static bool WidgetPreferenceApiAvailable {get;private set;}
  internal static string Get(string source) {
   if(source==null)return null;string[] pair;
   return Entries.TryGetValue(source,out pair)?pair[language=="ru"?1:0]:source;
  }
  internal static string ReadWidgetLanguage() {
   try {
    var manager=Type.GetType("Windows.Management.Core.ApplicationDataManager,Windows.Management,ContentType=WindowsRuntime",true);
    var data=manager.GetMethod("CreateForPackageFamily").Invoke(null,new object[]{"ONEXTweaks.Widget_3a23eh8tyg7na"});
    var dataType=Type.GetType("Windows.Storage.ApplicationData,Windows.Storage,ContentType=WindowsRuntime",true);
    var settings=dataType.GetProperty("LocalSettings").GetValue(data,null);
    var containerType=Type.GetType("Windows.Storage.ApplicationDataContainer,Windows.Storage,ContentType=WindowsRuntime",true);
    var values=(IDictionary<string,object>)containerType.GetProperty("Values").GetValue(settings,null);
    object selected;WidgetPreferenceApiAvailable=true;
    if(values.TryGetValue("OnexUiLanguage",out selected)){LanguageSource="widgetPreference";return object.Equals(selected,"ru")?"ru":"en";}
    LanguageSource="EnglishDefault";return "en";
   }catch(Exception error){ReadErrorType=error.GetType().Name;LanguageSource="EnglishDefault";return "en";}
  }
  internal static void Initialize(string[] args) {
   language=ReadWidgetLanguage();
   if(Array.IndexOf(args,"--verify-localization")>=0){
    if(Array.IndexOf(args,"--locale-ru")>=0){language="ru";LanguageSource="forcedQa";}
    if(Array.IndexOf(args,"--locale-en")>=0){language="en";LanguageSource="forcedQa";}
   }
  }
  internal static int KnownCaptionCount {get{return Entries.Count;}}
  private static readonly Dictionary<string,string[]> Entries=new Dictionary<string,string[]> {
   {"Не оценено",new[]{"Not rated","Не оценено"}},
   {"Подходит",new[]{"Matches","Подходит"}},
   {"Приемлемо, не идеально",new[]{"Acceptable, not exact","Приемлемо, не идеально"}},
   {"Нужна доработка",new[]{"Needs adjustment","Нужна доработка"}},
   {"Пропустить",new[]{"Skip","Пропустить"}},
   {"Нужен набор из 24 образцов",new[]{"A set of 24 reference samples is required","Нужен набор из 24 образцов"}},
   {"Неверный цвет образца",new[]{"Invalid reference color","Неверный цвет образца"}},
   {"ONEXTweaks · Калибровка LED 0.3",new[]{"ONEXTweaks · LED Calibration 0.4.1","ONEXTweaks · Калибровка LED 0.4.1"}},
   {"Проверка интерфейса: подсветка не меняется.",new[]{"UI preview: LED lighting stays unchanged.","Проверка интерфейса: подсветка не меняется."}},
   {"Прошлые результаты сохранены. Нажми «Продолжить».",new[]{"Previous results are saved. Select Resume.","Прошлые результаты сохранены. Нажми «Продолжить»."}},
   {"Нажми «Начать». До этого подсветка не меняется.",new[]{"Select Start. LED lighting stays unchanged until then.","Нажми «Начать». До этого подсветка не меняется."}},
   {"Выбор зоны подсветки",new[]{"LED zone selection","Выбор зоны подсветки"}},
   {"Все 3 зоны",new[]{"All 3 zones","Все 3 зоны"}},
   {"Нижняя · кнопка меню",new[]{"Lower · menu button","Нижняя · кнопка меню"}},
   {"Шаг кнопок и стрелок",new[]{"Button and arrow step","Шаг кнопок и стрелок"}},
   {"Шаг 1 · точный",new[]{"Step 1 · fine","Шаг 1 · точный"}},
   {"Шаг 2 · небольшой",new[]{"Step 2 · small","Шаг 2 · небольшой"}},
   {"Шаг 5 · средний",new[]{"Step 5 · medium","Шаг 5 · средний"}},
   {"Шаг 10 · крупный",new[]{"Step 10 · large","Шаг 10 · крупный"}},
   {"Шаг 20 · очень крупный",new[]{"Step 20 · extra large","Шаг 20 · очень крупный"}},
   {"Тон",new[]{"Hue","Тон"}},
   {"Насыщенность",new[]{"Saturation","Насыщенность"}},
   {"Яркость цвета RGB",new[]{"RGB color intensity","Яркость цвета RGB"}},
   {"Уменьшить: ",new[]{"Decrease: ","Уменьшить: "}},
   {"Увеличить: ",new[]{"Increase: ","Увеличить: "}},
   {"Тон — сам оттенок. Насыщенность: 0% — без оттенка, 100% — насыщенный цвет. RGB: ниже — темнее, выше — светлее; яркость LED выбирается справа.",new[]{"Hue changes the color. Saturation: 0% is neutral, 100% is fully colored. RGB: lower is darker, higher is lighter; LED brightness is selected on the right.","Тон — сам оттенок. Насыщенность: 0% — без оттенка, 100% — насыщенный цвет. RGB: ниже — темнее, выше — светлее; яркость LED выбирается справа."}},
   {"Канал ",new[]{"Channel ","Канал "}},
   {"Точное значение ",new[]{"Exact value ","Точное значение "}},
   {"Экранный цвет для сравнения",new[]{"Screen reference color","Экранный цвет для сравнения"}},
   {"Этап яркости LED",new[]{"LED brightness stage","Этап яркости LED"}},
   {"Этап ",new[]{"Stage ","Этап "}},
   {"/4 · LED яркость ",new[]{"/4 · LED brightness ","/4 · LED яркость "}},
   {"Оценка зоны: ",new[]{"Zone rating: ","Оценка зоны: "}},
   {"Комментарий к этому цвету и уровню яркости:",new[]{"Comment for this color and brightness:","Комментарий к этому цвету и уровню яркости:"}},
   {"Комментарий к образцу",new[]{"Reference sample comment","Комментарий к образцу"}},
   {"Продолжить",new[]{"Resume","Продолжить"}},
   {"Начать",new[]{"Start","Начать"}},
   {"Назад",new[]{"Back","Назад"}},
   {"Дальше",new[]{"Next","Дальше"}},
   {"Сброс выбранных",new[]{"Reset selected","Сброс выбранных"}},
   {"Закрыть",new[]{"Close","Закрыть"}},
   {"Автовозврат при бездействии: ",new[]{"Idle restore in: ","Автовозврат при бездействии: "}},
   {" · данные сохраняются автоматически",new[]{" · changes save automatically"," · данные сохраняются автоматически"}},
   {"4 этапа по 24 цвета. Оценки каждой яркости независимы. Закрытие возвращает исходную подсветку.",new[]{"4 stages of 24 colors. Each brightness has its own ratings. Closing restores the original LED lighting.","4 этапа по 24 цвета. Оценки каждой яркости независимы. Закрытие возвращает исходную подсветку."}},
   {"Сохраняю исходную подсветку…",new[]{"Saving original LED lighting…","Сохраняю исходную подсветку…"}},
   {"Применяю последнее положение ползунков…",new[]{"Applying the latest slider position…","Применяю последнее положение ползунков…"}},
   {"Цвет применён. Оцени все три зоны и нажми «Дальше».",new[]{"Color applied. Rate all three zones and select Next.","Цвет применён. Оцени все три зоны и нажми «Дальше»."}},
   {"Не удалось сохранить изменения. Закрой окно для возврата исходной подсветки.",new[]{"Could not save changes. Close the window to restore the original LED lighting.","Не удалось сохранить изменения. Закрой окно для возврата исходной подсветки."}},
   {"/4 · оценено ",new[]{"/4 · rated ","/4 · оценено "}},
   {"Завершить",new[]{"Finish","Завершить"}},
   {"Сохраняю результаты и возвращаю подсветку…",new[]{"Saving results and restoring LED lighting…","Сохраняю результаты и возвращаю подсветку…"}},
   {"ONEXTweaks · Калибратор LED",new[]{"ONEXTweaks · LED Calibrator","ONEXTweaks · Калибратор LED"}},
   {"Прошлый файл сохранён, но продолжить его автоматически не удалось.",new[]{"The previous file is preserved, but it could not be resumed automatically.","Прошлый файл сохранён, но продолжить его автоматически не удалось."}},
   {"Контроллер подсветки недоступен",new[]{"LED controller unavailable","Контроллер подсветки недоступен"}},
   {"Версия цветового преобразования помощника изменилась. Проверка остановлена.",new[]{"The helper color conversion version changed. The check has stopped.","Версия цветового преобразования помощника изменилась. Проверка остановлена."}},
   {"Калибровка уже запущена",new[]{"Calibration is already running","Калибровка уже запущена"}},
   {"Сессия проверки не активна",new[]{"The calibration session is not active","Сессия проверки не активна"}},
   {"Подсветка изменена вне калибратора. Новые настройки сохранены, проверка остановлена.",new[]{"LED lighting changed outside the calibrator. New settings are preserved; the check has stopped.","Подсветка изменена вне калибратора. Новые настройки сохранены, проверка остановлена."}},
   {"Помощник подтвердил другое состояние подсветки",new[]{"The helper acknowledged a different LED lighting state","Помощник подтвердил другое состояние подсветки"}},
   {"Подсветка не менялась",new[]{"LED lighting was not changed","Подсветка не менялась"}},
   {"Восстановление подсветки не подтверждено",new[]{"LED lighting restoration was not confirmed","Восстановление подсветки не подтверждено"}},
   {"Исходная подсветка восстановлена",new[]{"Original LED lighting restored","Исходная подсветка восстановлена"}},
   {"Сохранены более новые настройки подсветки",new[]{"Newer LED lighting settings preserved","Сохранены более новые настройки подсветки"}},
   {"Проверь состояние подсветки; страховка остаётся активной",new[]{"Check LED lighting; recovery protection remains active","Проверь состояние подсветки; страховка остаётся активной"}},
   {"Не удалось выполнить подготовку или восстановление. Подробности сохранены локально.",new[]{"Preparation or restoration failed. Details were saved locally.","Не удалось выполнить подготовку или восстановление. Подробности сохранены локально."}},
   {"Подключение к помощнику закрыто",new[]{"The helper connection is closed","Подключение к помощнику закрыто"}},
   {"Нет подключения к помощнику",new[]{"No helper connection","Нет подключения к помощнику"}},
   {"Предыдущая команда ещё выполняется",new[]{"The previous command is still running","Предыдущая команда ещё выполняется"}},
   {"Помощник не подтвердил команду за 15 секунд",new[]{"The helper did not acknowledge the command within 15 seconds","Помощник не подтвердил команду за 15 секунд"}},
   {"Верхняя",new[]{"Upper","Верхняя"}},
   {"Стики",new[]{"Sticks","Стики"}},
   {"Нижняя",new[]{"Lower","Нижняя"}},
   {"Красный",new[]{"Red","Красный"}},
   {"Оранжевый",new[]{"Orange","Оранжевый"}},
   {"Жёлтый",new[]{"Yellow","Жёлтый"}},
   {"Жёлто-зелёный",new[]{"Yellow-green","Жёлто-зелёный"}},
   {"Зелёный",new[]{"Green","Зелёный"}},
   {"Зелёно-бирюзовый",new[]{"Green-turquoise","Зелёно-бирюзовый"}},
   {"Аква",new[]{"Aqua","Аква"}},
   {"Лазурный",new[]{"Azure","Лазурный"}},
   {"Синий",new[]{"Blue","Синий"}},
   {"Фиолетовый",new[]{"Violet","Фиолетовый"}},
   {"Маджента",new[]{"Magenta","Маджента"}},
   {"Малиновый",new[]{"Raspberry","Малиновый"}},
   {"Персиковый",new[]{"Peach","Персиковый"}},
   {"Розовый",new[]{"Pink","Розовый"}},
   {"Светло-жёлтый",new[]{"Light yellow","Светло-жёлтый"}},
   {"Мятный",new[]{"Mint","Мятный"}},
   {"Бирюзовый",new[]{"Turquoise","Бирюзовый"}},
   {"Небесный",new[]{"Sky blue","Небесный"}},
   {"Лавандовый",new[]{"Lavender","Лавандовый"}},
   {"Сиреневый",new[]{"Lilac","Сиреневый"}},
   {"Белый",new[]{"White","Белый"}},
   {"Светло-серый",new[]{"Light gray","Светло-серый"}},
   {"Серый",new[]{"Gray","Серый"}},
   {"Тёмно-серый",new[]{"Dark gray","Тёмно-серый"}},
  };
 }
}
