#include "Mainwindow.h"
#include "Mflashtab.h"

#include <QApplication>
#include <QFont>
#include <QFontDatabase>
#include <QMetaObject>
#include <QPixmap>
#include <QTimer>

extern "C" {
#include "Canlab_flash.h"
}

int main(int argc, char *argv[])
{
    QCoreApplication::setAttribute(Qt::AA_EnableHighDpiScaling);
    QCoreApplication::setAttribute(Qt::AA_UseHighDpiPixmaps);

    QApplication app(argc, argv);
    app.setApplicationName("CANLAB Thermal Studio");
    app.setOrganizationName("CANLAB");
    app.setStyle("Fusion");
    const int fontId = QFontDatabase::addApplicationFont(QStringLiteral(":/fonts/UbuntuSans-Bold.ttf"));
    if (fontId >= 0) {
        const QString family = QFontDatabase::applicationFontFamilies(fontId).value(0);
        app.setFont(QFont(family, 9));
    } else {
        app.setFont(QFont("Segoe UI", 9));
    }


    MFlash_SetLogCallback([](const char *message) {
        const QString line = QString::fromLocal8Bit(message);
        QMetaObject::invokeMethod(qApp, [line]() {
            for (auto *window : qApp->topLevelWidgets()) {
                if (auto *tab = window->findChild<MFlashTab *>())
                    tab->logPrint(line);
            }
        }, Qt::QueuedConnection);
    });

    MainWindow window;
    window.show();
    if (app.arguments().contains(QStringLiteral("--render-preview"))) {
        QTimer::singleShot(500, &app, [&window, &app]() {
            window.grab().save(QStringLiteral("thermal_studio_preview.png"));
            app.quit();
        });
    } else if (app.arguments().contains(QStringLiteral("--smoke-test"))) {
        QTimer::singleShot(500, &app, &QCoreApplication::quit);
    }
    return app.exec();
}


