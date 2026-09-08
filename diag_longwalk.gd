extends SceneTree

func _initialize():
	var main = load("res://scenes/Main.tscn").instantiate()
	get_root().add_child(main)
	await process_frame
	await process_frame

	var world_reg = main.get_node("/root/World")
	var wren = main.get_node("WorldLayer/Wren")
	var stick = world_reg.GetEntity("stick_0")

	# 2000px away — at Speed=120, that's 16.7s of pure walking, already
	# past the OLD flat 10s "unreachable" timeout, and no obstacles in
	# a straight line so this should be a clean, real, completable walk.
	wren.global_position = stick.global_position + Vector2(2000, 0)
	await process_frame

	wren.__TestAssignPickUpStick(stick.WorldId)
	print("assigned a 2000px walk to a stick — old flat timeout was 10s (would have failed by then)")

	var dist_at_start = wren.global_position.distance_to(stick.global_position)
	await create_timer(10.5).timeout # just past the OLD flat 10s timeout
	var dist_at_old_timeout = wren.global_position.distance_to(stick.global_position)
	print("distance at start: ", dist_at_start)
	print("distance just past the OLD 10s timeout mark: ", dist_at_old_timeout, " (should be meaningfully closer, not reset/stuck)")

	await create_timer(24.5).timeout # total ~35s — past the new distance-scaled timeout too
	print("final distance to stick: ", wren.global_position.distance_to(stick.global_position))
	quit()
