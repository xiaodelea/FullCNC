#include "StepperControl.h"

uint16_t SCHEDULE_BUFFER[SCHEDULE_BUFFER_LEN + 1];
byte SCHEDULE_ACTIVATIONS[SCHEDULE_BUFFER_LEN + 1];
byte CUMULATIVE_SCHEDULE_ACTIVATION = 0;

volatile byte SCHEDULE_START = 0;
volatile byte SCHEDULE_END = 0;

volatile byte ACTIVATIONS_CLOCK_MASK = 1 + 4;
volatile bool SCHEDULER_STOP_EVENT_FLAG = false;
volatile bool SCHEDULER_START_EVENT_FLAG = false;


ISR(TIMER1_OVF_vect) {
	TCNT1 = SCHEDULE_BUFFER[SCHEDULE_END];

	//pins go LOW here (pulse start)
	PORTB = SCHEDULE_ACTIVATIONS[SCHEDULE_END];
	/*interrupts();
	Serial.print("|T:");
	Serial.print(UINT16_MAX - SCHEDULE_BUFFER[SCHEDULE_END]);
	Serial.print("B:");
	Serial.println(PORTB);*/

	if (SCHEDULE_START == SCHEDULE_END) {
		//we are at schedule end
		TIMSK1 = 0;
		SCHEDULER_STOP_EVENT_FLAG = true;
	}
	else {
		++SCHEDULE_END;
	}
	delayMicroseconds(3);
	//pins go HIGH here (pulse end)
	PORTB |= ACTIVATIONS_CLOCK_MASK;
}

bool Steppers::startScheduler() {
	if (TIMSK1 != 0) {
		//scheduler is already enabled
		//we are free to do other things
		return true;
	}

	if (SCHEDULE_START == SCHEDULE_END)
		//schedule is empty - no point in schedule enabling
		return false;


	Serial.print('S'); //enabling scheduler

	SCHEDULER_START_EVENT_FLAG = true;
	TCNT1 = SCHEDULE_BUFFER[SCHEDULE_END++];
	TIMSK1 = (1 << TOIE1); //enable scheduler

	return false;
}

void Steppers::initialize()
{
	noInterrupts(); // disable all interrupts
	TCCR1A = 0;
	TCCR1B = 0;
	TIMSK1 = 0;

	TCCR1B |= 1 << CS11; // 8 prescaler
	interrupts(); // enable all interrupts
}


AccelerationPlan::AccelerationPlan(byte clkPin, byte dirPin)
	: Plan(clkPin, dirPin), _currentDeltaT(0), _current2N(0), _currentDeltaTBuffer(0), _isDeceleration(false)
{
}

void AccelerationPlan::loadFrom(byte * data)
{
	int16_t stepCount = READ_INT16(data, 0);
	int32_t initialDeltaT = READ_INT32(data, 2);
	int16_t n = READ_INT16(data, 2 + 4);

	this->remainingSteps = abs(stepCount);
	this->isActive = this->remainingSteps > 0;
	this->stepMask = stepCount > 0 ? this->dirMask : 0;
	this->nextActivationTime = 0;
	this->isActivationBoundary = !this->isActive;

	this->_isDeceleration = n < 0;
	this->_currentDeltaT = initialDeltaT;
	this->_current2N = ((uint32_t)2) * abs(n);
	this->_currentDeltaTBuffer = 0;

	if (this->_isDeceleration && abs(n) < stepCount) {
		Serial.print('X');
		this->remainingSteps = 0;
	}
}

void AccelerationPlan::createNextActivation()
{
	if (this->remainingSteps == 0) {
		this->isActive = false;
		return;
	}

	--this->remainingSteps;

	int32_t nextDeltaT = this->_currentDeltaT;
	if (this->_isDeceleration) {
		this->_current2N -= 2;
		this->_currentDeltaTBuffer += nextDeltaT;
		while (this->_currentDeltaTBuffer * 2 > this->_current2N * 2 - 1) {
			this->_currentDeltaTBuffer -= (this->_current2N * 2 - 1) / 2;
			nextDeltaT += 1;
		}

		//Serial.print('|');
		//Serial.println(nextDeltaT);
	}
	else {
		this->_current2N += 2;
		this->_currentDeltaTBuffer += nextDeltaT;
		while (this->_currentDeltaTBuffer * 2 > this->_current2N * 2 - 1) {
			this->_currentDeltaTBuffer -= (this->_current2N * 2 - 1) / 2;
			nextDeltaT -= 1;
		}
	}
	this->_currentDeltaT = nextDeltaT;

	this->nextActivationTime = this->_currentDeltaT;
}

void ConstantPlan::createNextActivation()
{
	if (this->remainingSteps == 0) {
		this->isActive = false;
		return;
	}

	--this->remainingSteps;

	int32_t currentDeltaT = this->_baseDeltaT;

	if (this->_periodNumerator > 0) {
		this->_periodAccumulator += this->_periodNumerator;
		if (this->_periodDenominator >= this->_periodAccumulator) {
			this->_periodAccumulator -= this->_periodDenominator;
			currentDeltaT += 1;
		}
	}

	this->nextActivationTime = currentDeltaT;
}

ConstantPlan::ConstantPlan(byte clkPin, byte dirPin)
	:Plan(clkPin, dirPin),
	_baseDeltaT(0), _periodNumerator(0), _periodDenominator(0), _periodAccumulator(0)
{
}

void ConstantPlan::loadFrom(byte * data)
{
	int16_t stepCount = READ_INT16(data, 0);
	int32_t baseDeltaT = READ_INT32(data, 2);
	uint16_t periodNumerator = READ_UINT16(data, 2 + 4);
	uint16_t periodDenominator = READ_UINT16(data, 2 + 4 + 2);

	this->remainingSteps = abs(stepCount);
	this->stepMask = stepCount > 0 ? this->dirMask : 0;
	this->isActive = this->remainingSteps > 0;
	this->nextActivationTime = 0;
	this->isActivationBoundary = !this->isActive;

	this->_baseDeltaT = baseDeltaT;
	this->_periodNumerator = periodNumerator;
	this->_periodDenominator = periodDenominator;
	this->_periodAccumulator = 0;
}

Plan::Plan(byte clkPin, byte dirPin) :
	clkMask(PIN_TO_MASK(clkPin)), dirMask(PIN_TO_MASK(dirPin)),
	stepMask(0), remainingSteps(0), isActive(false), isActivationBoundary(false), nextActivationTime(0)
{
}
