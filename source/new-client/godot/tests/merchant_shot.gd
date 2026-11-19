extends Node
## Test (owner 2026-10-29: the original merchant window's SELL / APPRAISE buttons): the merchant window open with a
## shop list, a screenshot of it. --skip-login --zone=0 --no-npcs --probe=merchant_shot --out=<abs png>

func _ready() -> void:
	await get_tree().create_timer(10.0).timeout
	var v := get_tree().current_scene
	v.ui.merchant.open_for("Test Merchant", "HibWeaponMerchant5")
	v.ui.merchant.select_item(String(v.ui.merchant.pages[0][0]))
	await get_tree().create_timer(1.0).timeout
	await RenderingServer.frame_post_draw
	var out := ""
	for a in OS.get_cmdline_user_args():
		if a.begins_with("--out="):
			out = a.substr(6)
	get_viewport().get_texture().get_image().save_png(out)
	print("[merchant] saved ", out)
	get_tree().quit()
