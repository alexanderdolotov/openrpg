extends SceneTree

func _initialize():
	var main = load("res://scenes/Main.tscn").instantiate()
	get_root().add_child(main)
	await process_frame
	await process_frame

	var world_reg = main.get_node("/root/World")
	var player = main.get_node("WorldLayer/Player")
	var wolf = world_reg.GetEntity("animal_4")
	var health_bar = main.get_node("UI/VitalsPanel/HealthBar")

	for i in range(4):
		var r = world_reg.GetEntity("animal_%d" % i)
		if r: r.position = Vector2(5000, 5000)

	wolf.Hunger = 5.0
	wolf.position = player.position + Vector2(20, 0)
	print("start health_bar.value: ", health_bar.value)

	var last_value = health_bar.value
	var saw_decrease_while_not_idle = false
	for i in range(30):
		await create_timer(0.5).timeout
		var cur = health_bar.value
		print("t=", (i+1)*0.5, " health_bar.value=", cur, " player.state=", player.get("_state") if player.has_method("get") else "n/a")
		if cur < last_value:
			saw_decrease_while_not_idle = true
		last_value = cur

	print("health_bar.value ever decreased during the test: ", saw_decrease_while_not_idle)
	print("final health_bar.value: ", health_bar.value)

	quit()
