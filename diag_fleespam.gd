extends SceneTree

func _initialize():
	var main = load("res://scenes/Main.tscn").instantiate()
	get_root().add_child(main)
	await process_frame
	await process_frame

	var world_reg = main.get_node("/root/World")
	var rabbit = world_reg.GetEntity("animal_0")
	var wolf_a = world_reg.GetEntity("animal_4")
	var wolf_b = world_reg.GetEntity("animal_5")

	# Two wolves at nearly identical distance from the same rabbit —
	# exactly the "nearest threat flickers between them" setup that
	# used to spam a flee log line every physics tick.
	rabbit.position = Vector2(2000, 2000)
	wolf_a.position = rabbit.position + Vector2(50, 0)
	wolf_b.position = rabbit.position + Vector2(-50, 0)
	rabbit.Hunger = rabbit.MaxHunger # don't let hunger-seeking distract from fleeing
	await process_frame

	for i in range(120):
		await physics_frame

	print("done — check stdout above for how many 'flees from' lines appeared during 120 physics ticks")
	quit()
