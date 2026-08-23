dofile("$SURVIVAL_DATA/Scripts/game/survival_items.lua")

Freezer = class( nil )
Freezer.poseWeightCount = 1

local ProduceTickTime = DAYCYCLE_TIME_TICKS * 0.06

local NumConsumed = 1
local NumProduced = 20
local MaximumStored = 500

local LootSpawnHeightOffset = 0.8
local LootBubbleRadius = 0.3


function Freezer.server_onCreate( self )
    self.sv = {}
    self.sv.saved = self.storage:load()
    if self.sv.saved == nil then
        self.sv.container = self.shape:getInteractable():addContainer( 0, 1, 20 )
        self.sv.container:setFilters( { obj_consumable_water } )
        self.sv.saved = { 
            progress = 0,
            lastTickUpdate = sm.game.getCurrentTick(),
            ice = 0,
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

function Freezer.server_onUnload( self )
    if self.sv.loaded then
		self.sv.loaded = false
	end
end

function Freezer.server_onDestroy( self )
    local remainingOutput = self.sv.saved.ice + self.sv.saved.pendingPhysicalOutput
    if self.sv.loaded and remainingOutput > 0 and self.position and self.rotation then
        local stackSize = sm.item.getStackSize( ITEMS.blk_ice )
        while remainingOutput > 0 do
            local quant = min( stackSize, remainingOutput )
            remainingOutput = remainingOutput - quant

            local projectileParams = { lootUid = ITEMS.blk_ice, lootQuantity = quant, epic = false }
            local projectileDirection = self.rotation * ( sm.vec3.new( 0,1,0 ) + ( RandomUnitVector() * 0.1 ) )
            local projectilePosition = self.position + projectileDirection * LootSpawnHeightOffset
            sm.projectile.customProjectileAttack( projectileParams, projectile_loot, 0, projectilePosition, projectileDirection * 4, self.sv.world )
        end
        self.sv.loaded = false
    end
end

function Freezer.sv_container_onTransaction( self, container )
    self:sv_setClientData()
end

function Freezer.sv_spawnPhysicalOutput( self )
    local stackSize = sm.item.getStackSize( ITEMS.blk_ice )
    while self.sv.saved.pendingPhysicalOutput > 0 do
        local quantity = math.min( stackSize, self.sv.saved.pendingPhysicalOutput )
        local position = self.shape.worldPosition + ( self.shape.worldRotation * sm.vec3.new( 0, LootSpawnHeightOffset, 0 ) )
        local rotation = self.shape.worldRotation * sm.vec3.getRotation( sm.vec3.new( 0, 1, 0 ), sm.vec3.new( 0, 0, -1 ) )
        local loot = sm.harvestable.createHarvestable( hvs_loot, position, rotation )
        if not loot then
            return
        end

        loot:setParams( { uuid = ITEMS.blk_ice, quantity = quantity, epic = false } )
        self.sv.saved.pendingPhysicalOutput = self.sv.saved.pendingPhysicalOutput - quantity
        self.storage:save( self.sv.saved )
    end
end

function Freezer.sv_setClientData( self )
    self.network:setClientData( { active = self.sv.container:canSpend( obj_consumable_water, NumConsumed ), ice = self.sv.saved.ice } )
end

function Freezer.sv_updateProgress( self )
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
        return container:canSpend( obj_consumable_water, NumConsumed ) and NumProduced <= MaximumStored - self.sv.saved.pendingPhysicalOutput - additionalOutput
    end

    local remainingProgress = self.sv.saved.progress + elapsedTicks
    sm.container.beginTransaction()
    while remainingProgress >= ProduceTickTime and canProduce( produced ) do
        sm.container.spend( container, obj_consumable_water, NumConsumed, true )
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

function Freezer.server_onReceiveUpdate( self )
    self:sv_updateProgress()
end

function Freezer.sv_n_collect( self, args, player )
    if self.sv.saved.ice > 0 then
        if player and sm.exists( player ) then
            local inventory = player:getInventory()
            if sm.container.beginTransaction() then
                local amountCollected = inventory:collect( ITEMS.blk_ice, self.sv.saved.ice, false )
                if sm.container.endTransaction() and amountCollected > 0 then
                    self.sv.saved.ice = self.sv.saved.ice - amountCollected
                    self.storage:save( self.sv.saved )
                    self:sv_setClientData()

                    local pos = self.shape.worldPosition + ( self.shape.worldRotation * sm.vec3.new( 0, 0.4, 0 ) )
                    local rot = self.shape.worldRotation * sm.vec3.getRotation( sm.vec3.new( 0, 1, 0 ), sm.vec3.new( 0, 0, -1 ) )
                    sm.event.sendToPlayer( player, "sv_e_onLoot", { uuid = ITEMS.blk_ice, pos = pos, rot = rot } )
                end
            end
        end
    end
end

function Freezer.client_onCreate( self )
    self.cl = {}
    self.cl.freezingEffect = sm.effect.createEffect( "Interactive - Freezer_loop", self.interactable )
    self.cl.pose = 0
    self.cl.targetPose = 0
    self.cl.ice = nil
end

function Freezer.client_onDestroy( self )
    self.cl.freezingEffect:destroy()
    if self.cl.gui and sm.exists( self.cl.gui ) and self.cl.gui:isActive() then
        self.cl.gui:close()
        self.cl.gui:destroy()
        self.cl.gui = nil
    end
end

function Freezer.client_onClientDataUpdate( self, data )
    if data.active == true then
        if not self.cl.freezingEffect:isPlaying() or self.cl.freezingEffect:isBreakSustaining() then
            self.cl.freezingEffect:start()
        end
    else
        if self.cl.freezingEffect:isPlaying() and not self.cl.freezingEffect:isBreakSustaining() then
            self.cl.freezingEffect:stopBreakSustain()
        end
    end

    if self.cl.ice ~= nil and data.ice > self.cl.ice then
        sm.effect.playEffect( "Interactive - Freezer_done", self.shape.worldPosition + ( self.shape.worldRotation * sm.vec3.new( 0,0.4,0 ) ), nil, self.shape.worldRotation )
    end

    self.cl.ice = data.ice

    if self.cl.ice ~= nil and self.cl.ice > 0 then
        if not self.cl.iceTrigger then
            local offsetPosition = sm.vec3.new( 0,1,0 ) * LootSpawnHeightOffset

            self.cl.iceTrigger = sm.areaTrigger.createAttachedSphere( self.interactable, LootBubbleRadius, offsetPosition, nil, nil, nil, sm.areaTrigger.areaTriggerProxyType.interactable  )
            self.cl.iceLootEffect = sm.effect.createEffect( "Loot - GlowItem", self.interactable )
            self.cl.iceLootEffect:setParameter( "uuid", ITEMS.blk_ice )
		    self.cl.iceLootEffect:setParameter( "Color", sm.shape.getShapeTypeColor( ITEMS.blk_ice ) )
            self.cl.iceLootEffect:setScale( sm.vec3.new( 0.25, 0.25, 0.25 ) )
            self.cl.iceLootEffect:setOffsetPosition( offsetPosition )

            local randomRotation = sm.quat.angleAxis( math.random() * math.pi * 2, sm.vec3.new( 0, 1, 0 ) )
            self.cl.iceLootEffect:setOffsetRotation( randomRotation * sm.vec3.getRotation( sm.vec3.new( 0, 1, 0 ), sm.vec3.new( 0, 0, -1 ) ) )
            self.cl.iceLootEffect:start()

            self.cl.iceTrigger:bindCanInteract( "cl_trigger_canInteract" )
            self.cl.iceTrigger:bindCanErase( "cl_trigger_canErase" )
            self.cl.iceTrigger:bindOnInteract( "cl_trigger_onInteract" )
            self.cl.iceTrigger:bindOnErase( "cl_trigger_onErase" )

            self.cl.iceTrigger:setEraseTime( 0.0 )
            self.cl.iceTrigger:setDestroyOnErase( false )
        end
    else
        if self.cl.iceTrigger then
            if sm.exists( self.cl.iceTrigger ) then
                self.cl.iceTrigger:destroy()
            end
            self.cl.iceTrigger = nil
        end
        if self.cl.iceLootEffect then
            if sm.exists( self.cl.iceLootEffect ) then
                self.cl.iceLootEffect:destroy()
            end
            self.cl.iceLootEffect = nil
        end
    end
end

function Freezer.client_onUpdate( self, dt )
    if self.cl.gui and sm.exists(self.cl.gui) and self.cl.gui:isActive() then
        self.cl.targetPose = 1
    else
        self.cl.targetPose = 0
    end

    if self.cl.pose ~= self.cl.targetPose then
        self.cl.pose = lerp( self.cl.pose or 0, self.cl.targetPose, dt * 10)
        self.cl.pose = clamp( self.cl.pose, 0, 1 )
        self.interactable:setPoseWeight( 0, self.cl.pose )
    end

    if sm.isHost and self.cl.ice ~= nil and self.cl.ice > 0 then
        self.position = self.shape.worldPosition
        self.rotation = self.shape.worldRotation
    end
end

function Freezer.client_canInteract( self )
    return self.shape:getInteractable():getContainer(0) ~= nil
end

function Freezer.client_onInteract( self, _, state )
    if self.shape:getInteractable():getContainer(0) then
        if state == true then
            if self.cl.gui == nil or not sm.exists( self.cl.gui ) then
                self.cl.gui = sm.gui.createContainerGui( true )
            end
            self.cl.gui:setText( "UpperName", sm.shape.getShapeUpperCaseTitle( self.shape.uuid ) )
            self.cl.gui:setContainer( "UpperGrid", self.shape:getInteractable():getContainer(0) )
            self.cl.gui:setText( "LowerName", "#{INVENTORY_TITLE}" )
            self.cl.gui:setContainer( "LowerGrid", sm.localPlayer.getInventory() )
            self.cl.gui:setOpenCloseEffect( "Gui - ChestOpen", "Gui - ChestClose" );
            self.cl.gui:open()
        end
    end
end

function Freezer.cl_trigger_canInteract( self )
	local keyBindingText = GetInteractionKeybinding()
	sm.gui.setInteractionText( "", keyBindingText, "#{INTERACTION_PICK_UP} #FFFFC0" .. sm.shape.getShapeTitle( ITEMS.blk_ice ) .. "#FFFFFF"..( self.cl.ice > 1 and (" x " .. self.cl.ice ) or "" ) )
	return true
end

function Freezer.cl_trigger_canErase( self )
    return true
end

function Freezer.cl_trigger_onInteract( self, _, state )
    if state then
		self.network:sendToServer( "sv_n_collect" )
	end
end

function Freezer.cl_trigger_onErase( self )
    self.network:sendToServer( "sv_n_collect" )
end





