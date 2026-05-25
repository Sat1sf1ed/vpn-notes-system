using Microsoft.VisualStudio.TestTools.UnitTesting;

// Запрет параллельного выполнения тестов на уровне всей сборки.
// Все тесты используют одну тестовую БД vpnnotes_test, параллелизм
// приведёт к гонкам данных и непредсказуемым падениям.
[assembly: DoNotParallelize]