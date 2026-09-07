extends SceneTree

func _initialize():
	var main = load("res://scenes/Main.tscn").instantiate()
	get_root().add_child(main)
	await process_frame
	await process_frame
	await physics_frame

	var player = main.get_node("WorldLayer/Player")
	var sprite = player.get_node("Sprite")

	print("initial animation (expect idle_down): ", sprite.animation)

	# Drive real movement directly via velocity + MoveAndSlide, same
	# physics path the player's own _PhysicsProcess uses, then let
	# UpdateSpriteFacing (called every physics frame) react to it.
	var frames = sprite.sprite_frames
	for anim in ["idle_left","idle_right","idle_up","idle_down","walk_left","walk_right","walk_up","walk_down"]:
		print("has animation ", anim, ": ", frames.has_animation(anim), " frame count: ", frames.get_frame_count(anim) if frames.has_animation(anim) else -1)

	# Check each walk animation's frames are actually distinct columns
	# per direction and rows 0,1,2 within that column.
	for anim in ["walk_left","walk_right","walk_up","walk_down"]:
		var cols = []
		for i in range(frames.get_frame_count(anim)):
			var r = frames.get_frame_texture(anim, i).region
			cols.append(r.position.x / 16)
		print(anim, " columns used (expect same col x3): ", cols)

	quit()
