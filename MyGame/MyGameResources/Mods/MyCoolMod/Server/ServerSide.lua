local message = "Server hello!"

function on_load()
    G.log(message)
end

function on_unload()
    G.log("Unload Server")
end

function on_start()
    G.log("Start Server")
end

function on_update(dt)
    -- G.log("Server Tick" .. tostring(dt))
end