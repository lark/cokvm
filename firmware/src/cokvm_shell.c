#include <zephyr/shell/shell.h>
#include <zephyr/bluetooth/bluetooth.h>
#include <zephyr/bluetooth/conn.h>
#include <zephyr/logging/log.h>
#include "cokvm_bt.h"
#include "cokvm_ui.h"

#if IS_ENABLED(CONFIG_COKVM_TESTSUITE)
extern int cmd_cokvm_test_kbd(const struct shell *sh, size_t argc, char **argv);
extern int cmd_cokvm_test_mmove(const struct shell *sh, size_t argc, char **argv);
extern int cmd_cokvm_test_mbutton(const struct shell *sh, size_t argc, char **argv);
extern int cmd_cokvm_test_status(const struct shell *sh, size_t argc, char **argv);
extern int cmd_cokvm_test_peektx(const struct shell *sh, size_t argc, char **argv);
#endif

LOG_MODULE_REGISTER(cokvm_shell, LOG_LEVEL_INF);

static void bond_list_cb(const struct bt_bond_info *info, void *user_data)
{
    char addr_str[BT_ADDR_LE_STR_LEN];
    const struct shell *sh = (const struct shell *)user_data;

    bt_addr_le_to_str(&info->addr, addr_str, sizeof(addr_str));
    shell_print(sh, "%s", addr_str);
}

static int cmd_cokvm_pair(const struct shell *sh, size_t argc, char **argv)
{
    ARG_UNUSED(argc);
    ARG_UNUSED(argv);

    shell_print(sh, "open pairing window for 1 min");
    ck_bt_start_pairing_window();
    return 0;
}

static int cmd_cokvm_clear(const struct shell *sh, size_t argc, char **argv)
{
    ARG_UNUSED(argc);
    ARG_UNUSED(argv);

    bt_unpair(BT_ID_DEFAULT, BT_ADDR_LE_ANY);
    shell_warn(sh, "all bonds cleared");
    bt_set_bondable(false);
    ck_ui_set_status(COKVM_UI_STATUS_NORMAL);
    return 0;
}

static int cmd_cokvm_list(const struct shell *sh, size_t argc, char **argv)
{
    ARG_UNUSED(argc);
    ARG_UNUSED(argv);

    shell_print(sh, "bonded hosts:");
    bt_foreach_bond(BT_ID_DEFAULT, bond_list_cb, (void *)sh);
    return 0;
}

#if IS_ENABLED(CONFIG_COKVM_TESTSUITE)
SHELL_STATIC_SUBCMD_SET_CREATE(sub_cokvm_test,
    SHELL_CMD(kbd,    NULL, "Inject keyboard string (supports \\n,\\t,\\b,\\e,\\cX)", cmd_cokvm_test_kbd),
    SHELL_CMD(mmove,  NULL, "Move mouse to absolute coordinates x,y", cmd_cokvm_test_mmove),
    SHELL_CMD(mbutton, NULL, "Press mouse button(s) left/right/middle", cmd_cokvm_test_mbutton),
    SHELL_CMD(status, NULL, "Print current system status", cmd_cokvm_test_status),
    SHELL_CMD(peektx, NULL, "Peek USB TX queue head", cmd_cokvm_test_peektx),
    SHELL_SUBCMD_SET_END
);
#endif

SHELL_STATIC_SUBCMD_SET_CREATE(sub_cokvm,
    SHELL_CMD(pair,  NULL, "Open 60s BLE pairing window.", cmd_cokvm_pair),
    SHELL_CMD(clear, NULL, "Purge all saved host bonds.",   cmd_cokvm_clear),
    SHELL_CMD(list,  NULL, "Query and print bond list.",   cmd_cokvm_list),
#if IS_ENABLED(CONFIG_COKVM_TESTSUITE)
    SHELL_CMD(test,  &sub_cokvm_test, "Test injection commands", NULL),
#endif
    SHELL_SUBCMD_SET_END
);

SHELL_CMD_REGISTER(cokvm, &sub_cokvm, "CoKVM Hardware Management Commands", NULL);
