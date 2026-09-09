extends SceneTree

func _initialize():
	var main = load("res://scenes/Main.tscn").instantiate()
	get_root().add_child(main)
	await process_frame
	await process_frame

	var player = main.get_node("WorldLayer/Player")
	var inv_label = main.get_node("UI/VitalsPanel/InventoryLabel")
	var panel = main.get_node("UI/ActionPanel")
	var pine_button = panel.get_node("GatherPineconeButton")
	var berry_button = panel.get_node("GatherBerryButton")

	# --- Pinecone ---
	player.position = Vector2(250, 470) # right next to pine_0 (250,450)
	await process_frame
	await physics_frame
	print("GatherPineconeButton visible near pine tree: ", pine_button.visible)

	var got_pinecone = false
	for attempt in range(5):
		player.position = Vector2(250, 470)
		await process_frame
		await physics_frame
		pine_button.emit_signal("pressed")
		await create_timer(4.0).timeout
		if inv_label.text.contains("pinecone"):
			got_pinecone = true
			break
	print("pinecone gathered (retried up to 5x for a possible fumble): ", got_pinecone, " -- inventory: ", inv_label.text)

	# --- Berry ---
	player.position = Vector2(700, 270) # right next to berry_0 (700,250, blueberry)
	await process_frame
	await physics_frame
	print("GatherBerryButton visible near berry bush: ", berry_button.visible)

	var got_berry = false
	for attempt in range(5):
		player.position = Vector2(700, 270)
		await process_frame
		await physics_frame
		berry_button.emit_signal("pressed")
		await create_timer(4.0).timeout
		if inv_label.text.contains("blueberry"):
			got_berry = true
			break
	print("blueberry gathered (retried up to 5x for a possible fumble): ", got_berry, " -- inventory: ", inv_label.text)

	quit()
