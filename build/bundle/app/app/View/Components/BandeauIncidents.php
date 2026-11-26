<?php

namespace App\View\Components;

use App\Services\BandeauIncidents as Donnees;
use Illuminate\View\Component;
use Illuminate\View\View;

class BandeauIncidents extends Component
{
    public array $initial;

    public function __construct()
    {
        try {
            $this->initial = Donnees::donnees();
        } catch (\Throwable) {
            $this->initial = ['visible' => false];   // le bandeau ne doit jamais casser une page
        }
    }

    public function render(): View
    {
        return view('components.bandeau-incidents');
    }
}