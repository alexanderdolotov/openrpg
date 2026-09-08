extends SceneTree

func _initialize():
	var main = load("res://scenes/Main.tscn").instantiate()
	get_root().add_child(main)
	await process_frame
	await process_frame

	var world_reg = main.get_node("/root/World")
	var rabbit = world_reg.GetEntity("animal_0")
	var grass = world_reg.GetEntity("grass_0")

	# Stand right next to real, available food — otherwise breaking the
	# resting commitment just legitimately falls through to "still
	# tired, nothing better to do" and re-rests anyway, which looks
	# identical from outside and wouldn't actually test the fix.
	rabbit.global_position = grass.global_position + Vector2(10, 0)
	await process_frame

	# Healthy hunger, low fatigue — should choose to rest, not eat.
	rabbit.Hunger = 90.0
	rabbit.Fatigue = 5.0
	await physics_frame
	await physics_frame
	print("after low fatigue + healthy hunger, state (expect Resting=6): ", rabbit.CurrentState)

	# Now let hunger crash to critical WHILE still resting, mid-nap.
	rabbit.Hunger = 10.0 # 10% of MaxHunger(100) — below the 15% critical cutoff
	await physics_frame
	await physics_frame
	print("after hunger crashes to critical mid-rest, state (expect NOT Resting=6): ", rabbit.CurrentState)

	quit()
