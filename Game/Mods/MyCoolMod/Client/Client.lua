local message = "Client hello!"

function on_load()
    G.log(message)
end

function on_unload()
    G.log("Unload Client")
end

function on_update(dt)
    -- G.log("Client Tick")
end