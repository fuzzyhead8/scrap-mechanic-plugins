dofile("$SURVIVAL_DATA/Scripts/game/survival_items.lua")

InteractableBeehive = class( nil )

local ProduceTickTime = DAYCYCLE_TIME_TICKS * 0.12

local NumConsumed = 1
local NumProduced = 1
local MaximumStored = 20

local LootSpawnHeightOffset = 0.8
local LootBubbleRadius = 0.3


function InteractableBeehive.server_onCreate( self )
    self.sv = {}
    self.sv.saved = self.storage:load()
    if self.sv.saved == nil then
        self.sv.container = self.shape:getInteractable():addContainer( 0, 1, 20 )
        self.sv.container:setFilters( { obj_resource_flower } )
        self.sv.saved = { 
            progress = 0,
            lastTickUpdate = sm.game.getCurrentTick(),
            beewax = 0,
            pendingPhysicalOutput = 0
        }
    else
        self.sv.container = self.shape:getInteractable():getContainer(0)
        self.sv.saved.pendingPhysicalOutput = self.sv.saved.pendingPhysicalOutput or 0
    end
    self.sv.container:bindOnTransaction( "sv_container_onTransaction" )
    
    self:sv_updateProgress()
    
    self.sv.world = self.shape.body:getWorld()
    self.sv.loaded = true
end

function InteractableBeehive.server_onUnload( self )
    if self.sv.loaded then
		self.sv.loaded = false
	end
end

function InteractableBeehive.server_onDestroy( self )
    local remainingOutput = self.sv.saved.beewax + self.sv.saved.pendingPhysicalOutput
    if self.sv.loaded and remainingOutput > 0 and self.position and self.rotation then
        local stackSize = sm.item.getStackSize( ITEMS.obj_resource_beewax )
        while remainingOutput > 0 do
            local quant = min( stackSize, remainingOutput )
            remainingOutput = remainingOutput - quant

            local projectileParams = { lootUid = ITEMS.obj_resource_beewax, lootQuantity = quant, epic = false }
            local projectileDirection = self.rotation * ( sm.vec3.new( 0,1,0 ) + ( RandomUnitVector() * 0.1 ) )
            local projectilePosition = self.position + projectileDirection * LootSpawnHeightOffset
            sm.projectile.customProjectileAttack( projectileParams, projectile_loot, 0, projectilePosition, projectileDirection * 4, self.sv.world )
        end
        self.sv.loaded = false
    end
end

function InteractableBeehive.sv_container_onTransaction( self, container )
    self:sv_setClientData()
end

function InteractableBeehive.sv_n_collect( self, args, player )
    if self.sv.saved.beewax > 0 then
        if player and sm.exists( player ) then
            local inventory = player:getInventory()
            if sm.container.beginTransaction() then
                local amountCollected = inventory:collect( ITEMS.obj_resource_beewax, self.sv.saved.beewax, false )
                if sm.container.endTransaction() and amountCollected > 0 then
                    self.sv.saved.beewax = self.sv.saved.beewax - amountCollected
                    self.storage:save( self.sv.saved )
                    self:sv_setClientData()

                    local pos = self.shape.worldPosition + ( self.shape.worldRotation * sm.vec3.new( 0, 0.4, 0 ) )
                    local rot = self.shape.worldRotation * sm.vec3.getRotation( sm.vec3.new( 0, 1, 0 ), sm.vec3.new( 0, 0, -1 ) )
                    sm.event.sendToPlayer( player, "sv_e_onLoot", { uuid = ITEMS.obj_resource_beewax, pos = pos, rot = rot } )
                end
            end
        end
    end
end

function InteractableBeehive.sv_spawnPhysicalOutput( self )
    local stackSize = sm.item.getStackSize( ITEMS.obj_resource_beewax )
    while self.sv.saved.pendingPhysicalOutput > 0 do
        local quantity = math.min( stackSize, self.sv.saved.pendingPhysicalOutput )
        local position = self.shape.worldPosition + ( self.shape.worldRotation * sm.vec3.new( 0, LootSpawnHeightOffset, 0 ) )
        local rotation = self.shape.worldRotation * sm.vec3.getRotation( sm.vec3.new( 0, 1, 0 ), sm.vec3.new( 0, 0, -1 ) )
        local loot = sm.harvestable.createHarvestable( hvs_loot, position, rotation )
        if not loot then
            return
        end

        loot:setParams( { uuid = ITEMS.obj_resource_beewax, quantity = quantity, epic = false } )
        self.sv.saved.pendingPhysicalOutput = self.sv.saved.pendingPhysicalOutput - quantity
        self.storage:save( self.sv.saved )
    end
end

function InteractableBeehive.sv_setClientData( self )
    self.network:setClientData( { active = self.sv.container:canSpend( obj_resource_flower, NumConsumed ), beewax = self.sv.saved.beewax } )
end

function InteractableBeehive.sv_updateProgress( self )
    local currentTick = sm.game.getCurrentTick()
	local elapsedTicks = currentTick - self.sv.saved.lastTickUpdate
	elapsedTicks = math.max( elapsedTicks, 0 )
	self.sv.saved.lastTickUpdate = currentTick

    local container = self.shape:getInteractable():getContainer(0)
    if not container then
        return
    end

    self.position = self.shape.worldPosition
    self.rotation = self.shape.worldRotation

    local produced = 0
    local canProduce = function( additionalOutput )
        return container:canSpend( obj_resource_flower, NumConsumed ) and NumProduced <= MaximumStored - self.sv.saved.pendingPhysicalOutput - additionalOutput
    end

    local remainingProgress = self.sv.saved.progress + elapsedTicks
    sm.container.beginTransaction()
    while remainingProgress >= ProduceTickTime and canProduce( produced ) do
        sm.container.spend( container, obj_resource_flower, NumConsumed, true )
        remainingProgress = remainingProgress - ProduceTickTime
        produced = produced + NumProduced
    end

    if sm.container.endTransaction() then
        self.sv.saved.progress = remainingProgress
        self.sv.saved.pendingPhysicalOutput = self.sv.saved.pendingPhysicalOutput + produced
        if not canProduce( 0 ) then
            self.sv.saved.progress = 0
        end
    else
        self.sv.saved.progress = 0
    end

    self.storage:save( self.sv.saved )
    self:sv_spawnPhysicalOutput()
    self:sv_setClientData()
end

function InteractableBeehive.server_onReceiveUpdate( self )
    self:sv_updateProgress()
end

function InteractableBeehive.client_onCreate( self )
    self.cl = {}
    self.cl.beeEffect = sm.effect.createEffect( "Interactive - Beehive_loop", self.interactable )
    self.cl.beewax = nil
end

function InteractableBeehive.client_onDestroy( self )
    self.cl.beeEffect:destroy()
    if self.cl.gui and sm.exists( self.cl.gui ) and self.cl.gui:isActive() then
        self.cl.gui:close()
        self.cl.gui:destroy()
        self.cl.gui = nil
    end
end

function InteractableBeehive.client_onClientDataUpdate( self, data )
    if data.active == true then
        if not self.cl.beeEffect:isPlaying() then
            self.cl.beeEffect:start()
        end
    else
        if self.cl.beeEffect:isPlaying() then
            self.cl.beeEffect:stop()
        end
    end

    if self.cl.beewax ~= nil and data.beewax > self.cl.beewax then
        sm.effect.playEffect( "Interactive - Beehive_done", self.shape.worldPosition + ( self.shape.worldRotation * sm.vec3.new( 0,0.4,0 ) ), nil, self.shape.worldRotation )
    end

    self.cl.beewax = data.beewax

    if self.cl.beewax ~= nil and self.cl.beewax > 0 then
        if not self.cl.beewaxTrigger then
            local offsetPosition = sm.vec3.new( 0,1,0 ) * LootSpawnHeightOffset

            self.cl.beewaxTrigger = sm.areaTrigger.createAttachedSphere( self.interactable, LootBubbleRadius, offsetPosition, nil, nil, nil, sm.areaTrigger.areaTriggerProxyType.interactable  )
            self.cl.beewaxEffect = sm.effect.createEffect( "Loot - GlowItem", self.interactable )
            self.cl.beewaxEffect:setParameter( "uuid", ITEMS.obj_resource_beewax )
		    self.cl.beewaxEffect:setParameter( "Color", sm.shape.getShapeTypeColor( ITEMS.obj_resource_beewax ) )
            self.cl.beewaxEffect:setScale( sm.vec3.new( 0.25, 0.25, 0.25 ) )
            self.cl.beewaxEffect:setOffsetPosition( offsetPosition )

            local randomRotation = sm.quat.angleAxis( math.random() * math.pi * 2, sm.vec3.new( 0, 1, 0 ) )
            self.cl.beewaxEffect:setOffsetRotation( randomRotation * sm.vec3.getRotation( sm.vec3.new( 0, 1, 0 ), sm.vec3.new( 0, 0, -1 ) ) )
            self.cl.beewaxEffect:start()

            self.cl.beewaxTrigger:bindCanInteract( "cl_trigger_canInteract" )
            self.cl.beewaxTrigger:bindCanErase( "cl_trigger_canErase" )
            self.cl.beewaxTrigger:bindOnInteract( "cl_trigger_onInteract" )
            self.cl.beewaxTrigger:bindOnErase( "cl_trigger_onErase" )

            self.cl.beewaxTrigger:setEraseTime( 0.0 )
            self.cl.beewaxTrigger:setDestroyOnErase( false )
        end
    else
        if self.cl.beewaxTrigger then
            if sm.exists( self.cl.beewaxTrigger ) then
                self.cl.beewaxTrigger:destroy()
            end
            self.cl.beewaxTrigger = nil
        end
        if self.cl.beewaxEffect then
            if sm.exists( self.cl.beewaxEffect ) then
                self.cl.beewaxEffect:destroy()
            end
            self.cl.beewaxEffect = nil
        end
    end
end

function InteractableBeehive.client_onUpdate( self, dt )
    if sm.isHost and self.cl.beewax ~= nil and self.cl.beewax > 0 then
        self.position = self.shape.worldPosition
        self.rotation = self.shape.worldRotation
    end
end

function InteractableBeehive.client_canInteract( self )
    return self.shape:getInteractable():getContainer(0) ~= nil
end

function InteractableBeehive.client_onInteract( self, _, state )
    if self.shape:getInteractable():getContainer(0) then
        if state == true then
            if self.cl.gui == nil or not sm.exists( self.cl.gui ) then
                self.cl.gui = sm.gui.createContainerGui( true )
            end
            self.cl.gui:setText( "UpperName", sm.shape.getShapeUpperCaseTitle( self.shape.uuid ) )
            self.cl.gui:setContainer( "UpperGrid", self.shape:getInteractable():getContainer(0) )
            self.cl.gui:setText( "LowerName", "#{INVENTORY_TITLE}" )
            self.cl.gui:setContainer( "LowerGrid", sm.localPlayer.getInventory() )
            self.cl.gui:open()
        end
    end
end

function InteractableBeehive.cl_trigger_canInteract( self )
	local keyBindingText =  GetInteractionKeybinding()
	sm.gui.setInteractionText( "", keyBindingText, "#{INTERACTION_PICK_UP} #FFFFC0" .. sm.shape.getShapeTitle( ITEMS.obj_resource_beewax ) .. "#FFFFFF"..( self.cl.beewax > 1 and (" x " .. self.cl.beewax ) or "" ) )
	return true
end

function InteractableBeehive.cl_trigger_canErase( self )
    return true
end

function InteractableBeehive.cl_trigger_onInteract( self, _, state )
    if state then
		self.network:sendToServer( "sv_n_collect" )
	end
end

function InteractableBeehive.cl_trigger_onErase( self )
    self.network:sendToServer( "sv_n_collect" )
end

