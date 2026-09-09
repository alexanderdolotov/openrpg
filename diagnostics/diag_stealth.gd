extends SceneTree

func _initialize():
	var main = load("res://scenes/Main.tscn").instantiate()
	get_root().add_child(main)
	await process_frame
	await process_frame

	var world_reg = main.get_node("/root/World")
	var debug_log = main.get_node("UI/DebugLog")

	var wolf = world_reg.GetEntity("animal_4")
	var rabbit = world_reg.GetEntity("animal_0")
	print("wolf found: ", wolf != null, " rabbit found: ", rabbit != null)
	if wolf == null or rabbit == null:
		print("SETUP FAILED")
		quit()
		return

	# Isolate them from everything else so this test only exercises the
	# stalk/notice mechanic — far from other rabbits/wolves/humans.
	rabbit.position = Vector2(4000, 4000)
	wolf.position = Vector2(4000, 4000) + Vector2(170, 0) # near the edge of the rabbit's own 200px DetectionRadius
	wolf.Hunger = 50.0 # hungry enough to hunt, not starving/full — a normal hunt, not a desperate one

	print("start: wolf ", wolf.position, " rabbit ", rabbit.position, " dist ", wolf.position.distance_to(rabbit.position))

	var start_len = debug_log.get_parsed_text().length()
	for i in range(90):
		await create_timer(0.5).timeout
		var d = wolf.position.distance_to(rabbit.position)
		if i % 4 == 0:
			print("t=", (i+1)*0.5, " dist=", d, " wolf.Velocity=", wolf.velocity.length(), " rabbit.CurrentState=", rabbit.CurrentState)

	var full_log = debug_log.get_parsed_text().substr(start_len)
	print("=== LOG ===")
	print(full_log)
	print("=== END ===")
	print("stalking logged? ", full_log.contains("stalking"))
	print("notice roll logged? ", full_log.contains("notices"))
	print("caught (bites/brings down)? ", full_log.contains("bites Rabbit") or full_log.contains("brings down"))
	print("rabbit fled successfully (flees log)? ", full_log.contains("flees from"))

	quit()
